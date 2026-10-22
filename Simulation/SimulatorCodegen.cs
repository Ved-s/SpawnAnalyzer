using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace SpawnAnalyzer.Simulation;

public class SimulatorCodegen
{
    static ulong CodegenCounter = 0;
    readonly ulong CodegenID;

    public readonly DynamicMethodDefinition Method;
    public readonly ILContext IL;

    public readonly TempVarAllocator TempLocals;

    public DynamicStateType? LocalStateType;

    public readonly ParameterDefinition SimulatorParam;
    public readonly VariableDefinition NodeHitParamVar;

    private readonly ParameterDefinition entryParam;
    private readonly VariableDefinition stackStateVar;
    private readonly VariableDefinition localStateVar;

    private readonly IParameterHandler parameterHandler;

    public StackAnalysis? Stack;

    public List<MethodInfo> GeneratedHelperMethods = new();
    public List<SimulationNodeInfo> Nodes = new();

    public bool DebugPrintout = false;

    private List<ILLabel> entryJumps = new();
    private static ulong stackStatesGenerated = 0;
    private int LocalsToSave;

    public SimulatorCodegen(DynamicMethodDefinition dmd, IParameterHandler parameterHandler)
    {
        CodegenID = CodegenCounter;
        CodegenCounter++;

        Method = dmd;
        IL = new(dmd.Definition);

        TempLocals = new(Method.Definition.Body.Variables);
        SimulatorParam = new("sim", ParameterAttributes.None, IL.Import(typeof(Simulator)));
        NodeHitParamVar = new(IL.Import(typeof(object)));

        entryParam = new("entry", ParameterAttributes.None, IL.Import(typeof(int?)));
        stackStateVar = new(IL.Import(typeof(object)));
        localStateVar = new(IL.Import(typeof(object)));

        IL.PrepareMethodForManipulation();
        this.parameterHandler = parameterHandler;
    }

    public void AnalyzeStack()
    {
        Stack = StackAnalyzer.Analyze(IL);
    }

    /// <summary>
    /// The method is ready for node codegen
    /// All local variables at this moment will be marked for saving
    /// </summary>
    public void PrepareForNodeCodegen()
    {
        AnalyzeStack();
        TempLocals.ReleaseAll();

        LocalsToSave = IL.Body.Variables.Count;
    }

    public SimulatedMethod GenerateMethod()
    {
        HandleArguments();
        HandleLocalSavestate();

        ILCursor c = new(IL);
        ILLabel mainEntryLabel = IL.DefineLabel();

        c.Emit(OpCodes.Ldnull);
        c.Emit(OpCodes.Stloc, NodeHitParamVar);

        c.Emit(OpCodes.Ldarg, SimulatorParam);
        c.Emit<Simulator>(OpCodes.Ldfld, "localState");
        c.Emit(OpCodes.Stloc, localStateVar);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_HasValue");
        c.Emit(OpCodes.Brfalse, mainEntryLabel);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_Value");

        c.Emit(OpCodes.Switch, entryJumps.ToArray());
        c.Emit(OpCodes.Ret);
        c.MarkLabel(mainEntryLabel);

        IL.Method.Parameters.Clear();
        IL.Method.Parameters.Add(entryParam);
        IL.Method.Parameters.Add(SimulatorParam);

        IL.Body.Variables.Add(NodeHitParamVar);
        IL.Body.Variables.Add(stackStateVar);
        IL.Body.Variables.Add(localStateVar);

        IL.FinishMethodAfterManipulation();

        for (int i = 0; i < Nodes.Count; i++)
        {
            Nodes[i].Offset = entryJumps[i].Target!.Offset;
        }

        if (DebugPrintout)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                Console.WriteLine($"nodes[{i}] = {node.node.GetType().Name} at IL_{node.Offset:x4}");
            }

            StackAnalysis? stack = null;
            Exception? stackException = null;
            try
            {
                stack = StackAnalyzer.Analyze(IL);
            }
            catch (Exception e)
            {
                stackException = e;
            }

            IL.FancyPrintout(stack?.instructions);

            if (stackException is not null)
            {
                Console.WriteLine($"Stack analysis exception: {stackException}");
            }
        }

        int size = 0;
        foreach (Instruction instr in IL.Instrs)
        {
            size += instr.GetSize();
        }

        float sizeKb = (float)size / 1024;

        string name = Method.Name ?? Method.OriginalMethod?.Name ?? "<no name>";

        Console.WriteLine($"{name} simulator codegen finished, code size: {sizeKb:0.00}kb, instructions: {IL.Instrs.Count}, locals: {LocalStateType!.Type.GetFields().Length}, nodes: {Nodes.Count}");

        DMDHack.SetNullOriginalMethod(Method);

        MethodInfo newMethod = Method.Generate();

        return newMethod.CreateDelegate<SimulatedMethod>();
    }

    public void EmitNode(ILCursor c, SimulationNodeImpl node, NodeParameterInputBehavior pb, ReadOnlySpan<StackValue> stackValuesToSave)
    {
        ILLabel afterStopHandler = c.DefineLabel();

        ILLabel entryJumpLabel = c.DefineLabel();
        entryJumps.Add(entryJumpLabel);

        // Save stack if needed

        // TODO: cache
        DynamicStateType? stackStateType = null;

        (Type, bool)[] stackSaveTypes = new (Type, bool)[stackValuesToSave.Length];

        if (stackValuesToSave.Length > 0)
        {
            for (int i = 0; i < stackValuesToSave.Length; i++)
            {
                StackValue sv = stackValuesToSave[i];
                if (sv.type is null)
                {
                    throw new InvalidOperationException("Can't preserve stack value of unknown type");
                }
                if (sv.simpleType == SimpleType.Reference)
                {
                    throw new InvalidOperationException("Can't preserve stack value of reference type");
                }

                bool save = true;
                if (sv.type == typeof(Simulator))
                {
                    save = false;
                }

                stackSaveTypes[i] = (sv.type, save);
            }

            stackStateType = DynamicStateType.Generate($"RandomCallStackState_{stackStatesGenerated}", stackSaveTypes.Where(v => v.Item2).Select(v => v.Item1));
            stackStatesGenerated++;

            MethodInfo stackSaveMethod = GenerateStackSaveMethod(stackSaveTypes, stackStateType.Type);
            c.Emit(OpCodes.Ldarg, SimulatorParam);
            c.Emit(OpCodes.Call, stackSaveMethod);

            GeneratedHelperMethods.Add(stackSaveMethod);
        }

        c.MarkLabel(entryJumpLabel);

        // Call Simulator.NodeHit

        c.Emit(OpCodes.Ldarg, SimulatorParam);
        c.Emit(OpCodes.Ldc_I4, Nodes.Count);
        switch (pb)
        {
            case NodeParameterInputBehavior.AlwaysNull:
                c.Emit(OpCodes.Ldnull);
                break;

            case NodeParameterInputBehavior.LoadFromParamVar:
                c.Emit(OpCodes.Ldloc, NodeHitParamVar);
                break;
        }

        VariableDefinition stopVar = TempLocals.AllocVariable(IL.Import(typeof(bool)));

        c.Emit(OpCodes.Ldloca, stopVar);
        c.Emit<Simulator>(OpCodes.Call, "NodeHit");
        c.Emit(OpCodes.Ldloc, stopVar);
        c.Emit(OpCodes.Brfalse, afterStopHandler);

        TempLocals.FreeVariable(stopVar);

        // Return if requested

        c.Emit(OpCodes.Pop);
        c.Emit(OpCodes.Ret);

        c.MarkLabel(afterStopHandler);

        // Restore stack if needed

        if (stackValuesToSave.Length > 0)
        {
            VariableDefinition returnValueVar = TempLocals.AllocVariable(IL.Import(typeof(int)));

            c.Emit(OpCodes.Stloc, returnValueVar);

            // TODO: don't create state type when nothing is being preserved
            c.Emit(OpCodes.Ldarg, SimulatorParam);
            c.Emit<Simulator>(OpCodes.Call, "GetLastNodeStackStateClone");
            c.Emit(OpCodes.Stloc, stackStateVar);

            int field = 0;
            for (int i = 0; i < stackSaveTypes.Length; i++)
            {
                var (type, save) = stackSaveTypes[i];
                if (save) {
                    c.Emit(OpCodes.Ldloc, stackStateVar);
                    c.Emit(OpCodes.Ldfld, Utils.GetFieldOrThrow(stackStateType!.Type, DynamicStateType.GetAutoFieldName(field)));
                    field++;
                }
                else if (type == typeof(Simulator)) {
                    c.Emit(OpCodes.Ldarg, SimulatorParam);
                }
            }

            c.Emit(OpCodes.Ldloc, returnValueVar);
            TempLocals.FreeVariable(returnValueVar);
        }

        SimulationNodeInfo info = new(stackStateType, node);
        Nodes.Add(info);
    }

    private static MethodInfo GenerateStackSaveMethod(IEnumerable<(Type, bool)> values, Type stackStateType)
    {
        Type[] parameters = values.Select(v => v.Item1).Append(typeof(Simulator)).ToArray();

        DynamicMethod dmd = new($"StackSave_{stackStateType.Name}", typeof(void), parameters);

        ILGenerator il = dmd.GetILGenerator();

        LocalBuilder stateVar = il.DeclareLocal(stackStateType);

        MethodInfo createInstance = (typeof(Activator)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "CreateInstance" && m.IsGenericMethod)
            ?? throw new MissingMethodException("Activator::CreateInstance<T>"))
            .MakeGenericMethod([stackStateType]);

        il.Emit(ROpCodes.Call, createInstance);
        il.Emit(ROpCodes.Stloc, stateVar);

        int field = 0;
        int arg = 0;
        foreach (var (_, save) in values)
        {
            if (save)
            {
                il.Emit(ROpCodes.Ldloc, stateVar);
                il.Emit(ROpCodes.Ldarg, arg);
                il.Emit(ROpCodes.Stfld, Utils.GetFieldOrThrow(stackStateType, DynamicStateType.GetAutoFieldName(field)));

                field++;
            }
            arg++;
        }

        il.Emit(ROpCodes.Ldarg, parameters.Length - 1);
        il.Emit(ROpCodes.Ldloc, stateVar);
        il.Emit(ROpCodes.Stfld, Utils.GetFieldOrThrow<Simulator>("stackState"));

        il.Emit(ROpCodes.Ret);

        return dmd;
    }

    private void HandleLocalSavestate()
    {
        IEnumerable<Type> localTypes = IL.Body.Variables.Take(LocalsToSave).Select(v => v.VariableType.ResolveReflection());
        DynamicStateType type = DynamicStateType.Generate($"MethodLocalVarState_{CodegenID}", localTypes);

        localStateVar.VariableType = IL.Import(type.Type);
        LocalStateType = type;

        MethodInfo StoreByRefMethod = Utils.GetMethodOrThrow<SimulatorCodegen>(nameof(StoreByRef));

        ILCursor c = new(IL);

        int local = 0;
        OpcodeValueAction ac = 0;

        while (c.TryGotoNext(MoveType.AfterLabel, x => x.MatchLocalOpcode(out ac, out local)))
        {
            if (local < 0 || local >= LocalsToSave)
            {
                continue;
            }
            c.Next!.OpCode = OpCodes.Ldloc;
            c.Next!.Operand = localStateVar;
            c.Index += 1;
            switch (ac)
            {
                case OpcodeValueAction.Load:
                    c.Emit(OpCodes.Ldfld, IL.Import(Utils.GetFieldOrThrow(type.Type, DynamicStateType.GetAutoFieldName(local))));
                    break;

                case OpcodeValueAction.Store:
                    c.Emit(OpCodes.Ldflda, IL.Import(Utils.GetFieldOrThrow(type.Type, DynamicStateType.GetAutoFieldName(local))));

                    Type localType = IL.Body.Variables[local].VariableType.ResolveReflection();

                    MethodInfo genericStore = StoreByRefMethod.MakeGenericMethod([localType]);
                    c.Emit(OpCodes.Call, genericStore);
                    break;

                case OpcodeValueAction.LoadRef:
                    c.Emit(OpCodes.Ldflda, IL.Import(Utils.GetFieldOrThrow(type.Type, DynamicStateType.GetAutoFieldName(local))));
                    break;
            }
        }

        List<VariableDefinition> extraVars = IL.Body.Variables.Skip(LocalsToSave).ToList();

        IL.Body.Variables.Clear();
        foreach (VariableDefinition v in extraVars)
        {
            IL.Body.Variables.Add(v);
        }

        LocalsToSave = 0;
    }

    private void HandleArguments()
    {
        ILCursor c = new(IL);

        int arg = 0;
        OpcodeValueAction ac = 0;

        while (c.TryGotoNext(MoveType.AfterLabel, x => x.MatchArgOpcode(out ac, out arg)))
        {
            if (arg < 0)
            {
                continue;
            }
            c.Remove();
            string name = IL.Method.Parameters[arg].Name;
            switch (ac)
            {
                case OpcodeValueAction.Load:
                    parameterHandler.HandleLdarg(c, SimulatorParam, arg, name);
                    break;

                case OpcodeValueAction.LoadRef:
                    parameterHandler.HandleLdarga(c, SimulatorParam, arg, name);
                    break;

                case OpcodeValueAction.Store:
                    parameterHandler.HandleStarg(c, SimulatorParam, arg, name);
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void StoreByRef<T>(T val, ref T r)
    {
        r = val;
    }
}

public enum NodeParameterInputBehavior
{
    AlwaysNull,
    LoadFromParamVar,
}

public class TempVarAllocator
{
    readonly IList<VariableDefinition> Variables;
    readonly List<VariableDefinition> freeVars = new();

    public TempVarAllocator(IList<VariableDefinition> variables)
    {
        Variables = variables;
    }

    public VariableDefinition AllocVariable(TypeReference type)
    {
        VariableDefinition v;
        for (int i = 0; i < freeVars.Count; i++)
        {
            v = freeVars[i];
            if (ReferenceEquals(v.VariableType, type) || type.FullName == v.VariableType.FullName)
            {
                freeVars.RemoveAt(i);
                return v;
            }
        }

        v = new(type);
        Variables.Add(v);
        return v;
    }

    public void FreeVariable(VariableDefinition var)
    {
        freeVars.Add(var);
    }

    /// <summary>
    /// Stop tracking existing variables
    /// </summary>
    public void ReleaseAll()
    {
        freeVars.Clear();
    }
}

public interface IParameterHandler
{
    void HandleLdarg(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName);
    void HandleLdarga(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName);
    void HandleStarg(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName);
}

public delegate FieldInfo ArgumentFieldResolver(int argIndex, string argName);

public class StaticdataFieldNameParameterHandler : IParameterHandler
{
    readonly ArgumentFieldResolver resolver;

    public StaticdataFieldNameParameterHandler(ArgumentFieldResolver resolver)
    {
        this.resolver = resolver;
    }

    void IParameterHandler.HandleLdarg(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName)
    {
        c.Emit(OpCodes.Ldarg, simulatorParam);
        c.Emit<Simulator>(OpCodes.Ldfld, nameof(Simulator.staticSimData));
        c.Emit(OpCodes.Ldfld, resolver(arg, argName));
    }

    void IParameterHandler.HandleLdarga(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName)
    {
        c.Emit(OpCodes.Ldarg, simulatorParam);
        c.Emit<Simulator>(OpCodes.Ldfld, nameof(Simulator.staticSimData));
        c.Emit(OpCodes.Ldflda, resolver(arg, argName));
    }

    void IParameterHandler.HandleStarg(ILCursor c, ParameterDefinition simulatorParam, int arg, string argName)
    {
        throw new NotSupportedException("starg not supported");
    }
}