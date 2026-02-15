using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Utilities;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

class RandomCallRewriter
{
    static int StackStatesGenerated = 0;

    readonly List<SimulationNodeInfo> nodes;
    readonly ParameterDefinition contextParam;
    readonly VariableDefinition stopVar;
    readonly VariableDefinition nodeParamVar;
    readonly VariableDefinition stackStateVar;
    readonly VariableDefinition tempIntVar;
    readonly List<ILLabel> entryJumps;
    readonly HashSet<FieldInfo> allowFields;
    readonly HashSet<MethodInfo> allowMethods;

    static Dictionary<Mono.Cecil.Cil.OpCode, EqualityType> ConditionalOpcodeEqualityTypes = new()
    {
        { OpCodes.Blt,      EqualityType.Lt },
        { OpCodes.Blt_S,    EqualityType.Lt },
        { OpCodes.Blt_Un,   EqualityType.Lt },
        { OpCodes.Blt_Un_S, EqualityType.Lt },

        { OpCodes.Ble,      EqualityType.Le },
        { OpCodes.Ble_S,    EqualityType.Le },
        { OpCodes.Ble_Un,   EqualityType.Le },
        { OpCodes.Ble_Un_S, EqualityType.Le },

        { OpCodes.Bgt,      EqualityType.Gt },
        { OpCodes.Bgt_S,    EqualityType.Gt },
        { OpCodes.Bgt_Un,   EqualityType.Gt },
        { OpCodes.Bgt_Un_S, EqualityType.Gt },

        { OpCodes.Bge,      EqualityType.Ge },
        { OpCodes.Bge_S,    EqualityType.Ge },
        { OpCodes.Bge_Un,   EqualityType.Ge },
        { OpCodes.Bge_Un_S, EqualityType.Ge },
    };

    public RandomCallRewriter(
        List<SimulationNodeInfo> nodes,
        ParameterDefinition contextParam,
        VariableDefinition stopVar,
        VariableDefinition randomParamVar,
        VariableDefinition stackStateVar,
        VariableDefinition tempIntVar,
        List<ILLabel> entryJumps,
        HashSet<FieldInfo> allowFields,
        HashSet<MethodInfo> allowMethods
    )
    {
        this.nodes = nodes;
        this.contextParam = contextParam;
        this.stopVar = stopVar;
        this.nodeParamVar = randomParamVar;
        this.stackStateVar = stackStateVar;
        this.tempIntVar = tempIntVar;
        this.entryJumps = entryJumps;
        this.allowFields = allowFields;
        this.allowMethods = allowMethods;
    }

    public void RewriteRandomCalls(ILCursor c, StackAnalysis stack, bool allowUnknownPatterns)
    {
        ulong unknownPatterns = 0;
        ulong knownPatterns = 0;

        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<UnifiedRandom>("Next")
              || x.MatchCall("Terraria.Utils", "SelectRandom")
              || x.MatchCallOrCallvirt(out MethodReference? mr) && mr.Name.StartsWith("Roll")
        ))
        {
            Instruction instr = c.Next!;
            MethodReference method = (instr.Operand as MethodReference)!;

            InstructionStackInfo stackInfo = stack.LookupInstruction(instr, out _)
                ?? throw new InvalidOperationException("Stack analysis out of date or invalid");

            Instruction? thisLoadInstr = null;

            bool treatFirstArgAsThis = method.Name == "SelectRandom";

            if (method.HasThis || treatFirstArgAsThis)
            {
                int restParams = method.Parameters.Count;
                if (treatFirstArgAsThis)
                {
                    restParams--;
                }
                StackValue value = stackInfo.inValues[stackInfo.inValues.Count - 1 - restParams];

                if (value.producedBy.Count != 1)
                    throw new InvalidOperationException($"Invalid this param value source for random call at IL_{instr.Offset:x4}");

                thisLoadInstr = value.producedBy[0];

                if (!thisLoadInstr.MatchLdsfld<Main>("rand") && !thisLoadInstr.MatchLdarg(0))
                    throw new InvalidOperationException($"Invalid this param value source (at IL_{thisLoadInstr.Offset:x4}) for random call at IL_{instr.Offset:x4}");
            }

            int passingStackValueCount = stackInfo.outValues.Count - 1;
            StackValue[] passingStackValues = new StackValue[passingStackValueCount];
            for (int vi = 0; vi < passingStackValueCount; vi++)
            {
                passingStackValues[vi] = stackInfo.outValues[vi];
            }

            c.Goto(instr.Next);
            ValueHandler? valueHandler = TryCreateValueHandler(c, stackInfo.outValues[stackInfo.outValues.Count - 1], allowUnknownPatterns);
            if (valueHandler is null)
            {
                SpawnAnalyzer.ReportUnknownPattern("random call value handler", c.Context, c.Instrs.IndexOf(instr) + 1, 2, 10);
                c.Goto(instr.Next);
                unknownPatterns++;
                continue;
            }

            if (method.Parameters.Count == 1 && method.Parameters[0].ParameterType.Is(typeof(int)))
            {
                c.Goto(instr.Previous);
                Instruction lastParamInitInstr = c.Next!;

                ParamProvider<int> param = CreateSingleIntProvider(c);

                SimulationNode? node = TryBuildSingleIntSimulationNode(method, param, valueHandler.Value);
                if (node is null)
                {
                    SpawnAnalyzer.ReportUnknownPattern("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(instr.Next);
                    unknownPatterns++;
                    continue;
                }

                if (thisLoadInstr is not null)
                {
                    c.Goto(thisLoadInstr);
                    c.Remove();
                }

                c.Goto(instr, MoveType.AfterLabel);
                c.Remove();
                EmitNode(c, node, param.ParameterInputBehavior, passingStackValues);

                knownPatterns++;
                continue;
            }

            if (method.Parameters.Count == 2
             && method.Parameters[0].ParameterType.Is(typeof(int))
             && method.Parameters[1].ParameterType.Is(typeof(int))
            )
            {
                c.Goto(instr.Previous);
                Instruction lastParamInitInstr = c.Next!;

                ParamProvider<(int, int)> param = CreateDoubleIntProvider(c);

                SimulationNode? node = TryBuildDoubleIntSimulationNode(method, param, valueHandler.Value);
                if (node is null)
                {
                    SpawnAnalyzer.ReportUnknownPattern("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(instr.Next);
                    unknownPatterns++;
                    continue;
                }

                if (thisLoadInstr is not null)
                {
                    c.Goto(thisLoadInstr);
                    c.Remove();
                }

                c.Goto(instr, MoveType.AfterLabel);
                c.Remove();
                EmitNode(c, node, param.ParameterInputBehavior, passingStackValues);

                knownPatterns++;
                continue;
            }

            if (method.Name == "SelectRandom")
            {
                Type valueType = method.ResolveReflection().GetGenericArguments()[0];

                if (!SelectRandomNode.SupportsArrayElementType(valueType))
                {
                    SpawnAnalyzer.ReportUnknownPattern("SelectRandom value type", c.Context, c.Instrs.IndexOf(instr), 10, 5);
                    c.Goto(instr.Next);
                    unknownPatterns++;
                    continue;
                }

                c.Goto(instr);
                c.Emit(OpCodes.Stloc, nodeParamVar);

                SimulationNode? node = SelectRandomNode.Build(valueHandler.Value);
                if (node is null)
                {
                    SpawnAnalyzer.ReportUnknownPattern("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(instr.Next);
                    unknownPatterns++;
                    continue;
                }

                if (thisLoadInstr is not null)
                {
                    c.Goto(thisLoadInstr);
                    c.Remove();
                }

                c.Goto(instr, MoveType.AfterLabel);
                c.Remove();
                EmitNode(c, node, NodeParameterInputBehavior.LoadFromParamVar, passingStackValues);

                knownPatterns++;
                continue;
            }

            SpawnAnalyzer.ReportUnknownPattern("random call", c.Context, c.Instrs.IndexOf(instr), 10, 5);
            c.Goto(instr.Next);
            unknownPatterns++;
            continue;
        }

        if (unknownPatterns > 0)
        {
            double percent = (double)knownPatterns / (knownPatterns + unknownPatterns) * 100;
            throw new Exception($"\n{percent:0.0}% ({knownPatterns}/{knownPatterns + unknownPatterns}) of Random calls were patched");
        }
    }

    private void EmitNode(ILCursor c, SimulationNode node, NodeParameterInputBehavior pb, StackValue[] stackValues)
    {
        ILLabel afterStopHandler = c.DefineLabel();

        ILLabel entryJumpLabel = c.DefineLabel();
        entryJumps.Add(entryJumpLabel);

        // TODO: cache
        StateType? stackStateType = null;

        List<StackValuePreserveType> stackValuePreserves = new();

        if (stackValues.Length > 0)
        {
            int? spawnNPCThisArg = null;
            List<Type> typesToPreserve = new();

            for (int i = 0; i < stackValues.Length; i++)
            {
                StackValue sv = stackValues[i];
                if (sv.type is null)
                {
                    throw new InvalidOperationException("Can't preserve stack value of unknown type");
                }
                if (sv.simpleType == SimpleType.Reference)
                {
                    throw new InvalidOperationException("Can't preserve stack value of reference type");
                }

                // TODO: optimize common patterns
                StackValuePreserveType pt = StackValuePreserveType.Preserve;

                if (spawnNPCThisArg is null
                    && sv.consumedBy.Count == 1
                    && sv.consumedBy[0].MatchCallOrCallvirt(out MethodReference? consumerMethod)
                    && consumerMethod.Name == "SpawnNPC"
                )
                {
                    spawnNPCThisArg = i;
                    pt = StackValuePreserveType.ContextParam;
                }

                if (pt == StackValuePreserveType.Preserve)
                {
                    typesToPreserve.Add(sv.type);
                }
                stackValuePreserves.Add(pt);
            }

            stackStateType = StateType.Generate($"RandomCallStackState_{StackStatesGenerated}", typesToPreserve);
            StackStatesGenerated++;

            MethodInfo stackSaveMethod = GenerateStackSaveMethod(stackValues, stackValuePreserves, stackStateType.Type);
            c.Emit(OpCodes.Ldarg, contextParam);
            c.Emit(OpCodes.Call, stackSaveMethod);

            allowMethods.Add(stackSaveMethod);

            // foreach (var _ in stackValuePreserves)
            // {
            //     c.Emit(OpCodes.Pop);
            // }
        }

        c.MarkLabel(entryJumpLabel);

        c.Emit(OpCodes.Ldarg, contextParam);

        c.Emit(OpCodes.Ldc_I4, nodes.Count);
        switch (pb)
        {
            case NodeParameterInputBehavior.AlwaysNull:
                c.Emit(OpCodes.Ldnull);
                break;

            case NodeParameterInputBehavior.LoadFromParamVar:
                c.Emit(OpCodes.Ldloc, nodeParamVar);
                break;
        }
        c.Emit(OpCodes.Ldloca, stopVar);
        c.Emit<SpawnSimulationContext>(OpCodes.Call, "NodeHit");
        c.Emit(OpCodes.Ldloc, stopVar);
        c.Emit(OpCodes.Brfalse, afterStopHandler);

        // rolledValue
        c.Emit(OpCodes.Pop);
        c.Emit(OpCodes.Ret);

        c.MarkLabel(afterStopHandler);

        if (stackValuePreserves.Count > 0)
        {
            c.Emit(OpCodes.Stloc, tempIntVar);


            // TODO: don't create state type when nothing is being preserved
            c.Emit(OpCodes.Ldarg, contextParam);
            c.Emit<SpawnSimulationContext>(OpCodes.Call, "GetLastNodeStackStateClone");
            c.Emit(OpCodes.Stloc, stackStateVar);

            int fieldIndex = 0;
            for (int i = 0; i < stackValuePreserves.Count; i++)
            {
                switch (stackValuePreserves[i])
                {
                    case StackValuePreserveType.Preserve:
                        c.Emit(OpCodes.Ldloc, stackStateVar);
                        c.Emit(OpCodes.Ldfld, Utils.GetFieldOrThrow(stackStateType!.Type, StateType.GetFieldName(fieldIndex)));
                        fieldIndex++;
                        break;

                    case StackValuePreserveType.ContextParam:
                        c.Emit(OpCodes.Ldarg, contextParam);
                        break;

                    default:
                        throw new NotImplementedException($"Restore StackValuePreserveType.{stackValuePreserves[i]} onto stack");
                }
            }

            c.Emit(OpCodes.Ldloc, tempIntVar);
        }

        SimulationNodeInfo info = new(stackStateType, node);

        nodes.Add(info);
    }

    private ParamProvider<int> CreateSingleIntProvider(ILCursor c)
    {
        int staticValue = 0;
        if (c.IncomingLabels.Count() == 0 
         && c.Index > 0 
         && SpawnAnalyzer.MatchInstructions(
            c.Context, c.Index, out _,
            x => x.MatchLdcI4(out staticValue)
        ))
        {
            c.Remove();

            return new StaticParamProvider<int>(staticValue);
        }

        c.Goto(c.Index + 1, MoveType.AfterLabel);

        c.Emit(OpCodes.Box, c.Context.Import(typeof(int)));
        c.Emit(OpCodes.Stloc, nodeParamVar);

        return new RuntimeParamVarCastParamProvider<int>();
    }

    private ParamProvider<(int, int)> CreateDoubleIntProvider(ILCursor c)
    {
        int staticValue0 = 0;
        int staticValue1 = 0;
        if (c.Index >= 1 && SpawnAnalyzer.MatchInstructions(
            c.Context, c.Index - 1, out _,
            x => x.MatchLdcI4(out staticValue0),
            x => x.MatchLdcI4(out staticValue1)
        ))
        {
            c.Goto(c.Index - 1);
            c.RemoveRange(2);

            return new StaticParamProvider<(int, int)>((staticValue0, staticValue1));
        }

        c.Index += 1;
        c.Emit<RandomCallRewriter>(OpCodes.Call, nameof(PackTwoIntTupleBoxed));
        c.Emit(OpCodes.Stloc, nodeParamVar);

        return new RuntimeParamVarCastParamProvider<(int, int)>();
    }

    private ValueHandler? TryCreateValueHandler(ILCursor c, StackValue value, bool allowUnknownPatterns)
    {
        Instruction ins = c.Next!;
        int index = c.Index;
        if (ins.MatchBrfalse(out _) || ins.MatchBrtrue(out _))
        {
            return new(ValueHandlerType.ZeroOrNonzero);
        }
        else if (ins.MatchStloc(out _) || ins.MatchAdd() || ins.MatchDup())
        {
            return new(ValueHandlerType.AllUnique);
        }
        else if (SpawnAnalyzer.MatchInstructions(
            c.Context, index, out _,
            x => x.MatchLdcI4(0),
            x => x.MatchBle(out _) || x.MatchBeq(out _) || x.MatchCeq() || x.MatchCgt() || x.MatchCgtUn()
        ))
        {
            return new(ValueHandlerType.ZeroOrNonzero);
        }

        /*
            call      int32 Terraria.NPC::CountNPCS(int32)
	        ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
	        ldc.i4.3
	        callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
	     -> bgt.s     IL_771F
        */

        else if (index >= 4 && SpawnAnalyzer.MatchInstructions(
            c.Context, index - 4, out _,
            x => x.MatchCall<NPC>("CountNPCS"),
            x => x.MatchLdsfld<Main>("rand"),
            x => x.MatchLdcI4(out _)
        ))
        {
            return new(ValueHandlerType.AllUnique);
        }
        else if (value.consumedBy.Count == 1 && value.consumedBy[0].MatchCallOrCallvirt(out MethodReference? mr) && (mr.Name == "SpawnNPC" || mr.Name == "Get"))
        {
            return new(ValueHandlerType.AllUnique);
        }

        int val = 0;
        EqualityType eqType = 0;

        if (SpawnAnalyzer.MatchInstructions(
            c.Context, index, out _,
            x => x.MatchLdcI4(out val),
            x => ConditionalOpcodeEqualityTypes.TryGetValue(x.OpCode, out eqType)
        ))
        {
            int beforeBreakpointVal = eqType switch
            {
                EqualityType.Lt => val - 1,
                EqualityType.Le => val,
                EqualityType.Gt => val,
                EqualityType.Ge => val - 1,
                _ => throw new IndexOutOfRangeException($"Unhandled EqualityType.{eqType} in TryCreateValueHandler")
            };

            return new(ValueHandlerType.LeValueOrGtValue, beforeBreakpointVal);
        }

        if (!allowUnknownPatterns)
        {
            return null;
        }

        Console.WriteLine($"Warning: Unknown random value handling pattern (at IL_{ins.Offset:x4}), simulation may take long time");
        return new(ValueHandlerType.AllUnique);
    }

    private SimulationNode? TryBuildSingleIntSimulationNode(MethodReference method, ParamProvider<int> param, ValueHandler valHandler)
    {
        if (method.Name == "Next")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.None);
        }
        if (method.Name == "RollLuck")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.GoodLuck);
        }
        if (method.Name == "RollBadLuck")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.BadLuck);
        }
        if (method.Name == "RollOnlyBadLuck")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.OnlyBadLuck);
        }
        if (method.Name == "RollBadLuckExtreme")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.BadLuckExtreme);
        }
        if (method.Name == "RollOnlyBadLuckExtreme")
        {
            return OneParamRandomNextNode.Build(param, valHandler, LuckDependance.OnlyBadLuckExtreme);
        }
        if (method.Name == "RollDragonflyType")
        {
            return new RandomDragonflyTypeNode(param);
        }
        return null;
    }

    private SimulationNode? TryBuildDoubleIntSimulationNode(MethodReference method, ParamProvider<(int, int)> param, ValueHandler valHandler)
    {
        if (method.Name == "Next")
        {
            return TwoParamRandomNextNode.Build(param, valHandler);
        }
        return null;
    }

    private static object PackTwoIntTupleBoxed(int a, int b)
    {
        return (a, b);
    }

    private static MethodInfo GenerateStackSaveMethod(StackValue[] values, List<StackValuePreserveType> preserves, Type stackStateType)
    {
        Type[] pt = new Type[values.Length + 1];

        for (int i = 0; i < values.Length; i++)
        {
            switch (preserves[i])
            {
                case StackValuePreserveType.Preserve:
                    pt[i] = values[i].type!;
                    break;

                case StackValuePreserveType.ContextParam:
                    pt[i] = typeof(object);
                    break;

                default:
                    throw new NotImplementedException($"GenerateStackSaveMethod StackValuePreserveType.{preserves[i]}");
            }
        }

        pt[pt.Length - 1] = typeof(SpawnSimulationContext);

        DynamicMethod dmd = new($"StackSave_{stackStateType.Name}", typeof(void), pt);

        ILGenerator il = dmd.GetILGenerator();

        LocalBuilder stateVar = il.DeclareLocal(stackStateType);

        MethodInfo createInstance = (typeof(Activator)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "CreateInstance" && m.IsGenericMethod)
            ?? throw new MissingMethodException("Activator::CreateInstance<T>"))
            .MakeGenericMethod([stackStateType]);

        il.Emit(ROpCodes.Call, createInstance);
        il.Emit(ROpCodes.Stloc, stateVar);

        int fieldIndex = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (preserves[i] == StackValuePreserveType.Preserve)
            {
                il.Emit(ROpCodes.Ldloc, stateVar);
                il.Emit(ROpCodes.Ldarg, i);
                il.Emit(ROpCodes.Stfld, Utils.GetFieldOrThrow(stackStateType, StateType.GetFieldName(fieldIndex)));

                fieldIndex++;
            }
        }

        il.Emit(ROpCodes.Ldarg, pt.Length - 1);
        il.Emit(ROpCodes.Ldloc, stateVar);
        il.Emit(ROpCodes.Stfld, Utils.GetFieldOrThrow<SpawnSimulationContext>("stackState"));

        il.Emit(ROpCodes.Ret);

        return dmd;
    }
}

enum StackValuePreserveType
{
    Preserve,
    ContextParam,
}

enum NodeParameterInputBehavior
{
    AlwaysNull,
    LoadFromParamVar,
}

abstract class ParamProvider<T>
{
    public abstract NodeParameterInputBehavior ParameterInputBehavior { get; }

    public abstract T Provide(object paramInput, NodeRollParams rollParams);
}

struct ValueRange
{
    public int start;

    public int? endExclusive;

    public ValueRange(int start, int? endExclusive)
    {
        this.start = start;
        this.endExclusive = endExclusive;
    }

    public uint? PositiveLength()
    {
        int? length = Length();
        if (length is null)
            return null;

        if (length.Value < 0)
            return 0;

        return (uint)length.Value;
    }
    public int? Length()
    {
        if (endExclusive is null)
            return null;

        return endExclusive.Value - start;
    }

    public uint? CalculateOverlap(ValueRange other)
    {
        return Overlap(other).PositiveLength();
    }

    //    ========       ====
    // ========            ====
    //    =====            ==
    public ValueRange Overlap(ValueRange other)
    {
        int newStart = Math.Max(start, other.start);
        int? newEnd;
        if (endExclusive is null)
            newEnd = other.endExclusive;
        else if (other.endExclusive is null)
            newEnd = endExclusive;
        else
            newEnd = Math.Min(endExclusive.Value, other.endExclusive.Value);

        return new ValueRange(newStart, newEnd);
    }

    public override string ToString()
    {
        if (endExclusive is null)
            return $"{start}..";
        else
            return $"{start}..{endExclusive.Value}";
    }
}

struct ValueHandler
{
    public ValueHandlerType type;

    public int value;

    public ValueHandler(ValueHandlerType type, int value = 0)
    {
        this.type = type;
        this.value = value;
    }
}

enum ValueHandlerType
{
    ZeroOrNonzero,
    LeValueOrGtValue,
    AllUnique,
}

class StaticParamProvider<T> : ParamProvider<T>
{
    readonly T value;

    public StaticParamProvider(T value)
    {
        this.value = value;
    }

    public override NodeParameterInputBehavior ParameterInputBehavior => NodeParameterInputBehavior.AlwaysNull;

    public override T Provide(object _paramInput, NodeRollParams _rollParams)
    {
        return value;
    }
}

class RuntimeParamVarCastParamProvider<T> : ParamProvider<T>
{
    public override NodeParameterInputBehavior ParameterInputBehavior => NodeParameterInputBehavior.LoadFromParamVar;

    public override T Provide(object paramInput, NodeRollParams _rollParams)
    {
        return (T)paramInput;
    }
}

// TODO: Eliminate 0% branches, Merge branches with same return value

class OneParamRandomNextNode : SimulationNode
{
    readonly ParamProvider<int> param;

    readonly ValueHandler handler;

    readonly LuckDependance luckDependance;

    private OneParamRandomNextNode(ParamProvider<int> param, ValueHandler handler, LuckDependance luckDependance)
    {
        this.param = param;
        this.handler = handler;
        this.luckDependance = luckDependance;
    }

    public static OneParamRandomNextNode? Build(ParamProvider<int> param, ValueHandler handler, LuckDependance luckDependance)
    {
        if (handler.type != ValueHandlerType.ZeroOrNonzero && handler.type != ValueHandlerType.LeValueOrGtValue && handler.type != ValueHandlerType.AllUnique)
        {
            return null;
        }
        if (luckDependance != LuckDependance.None && handler.type != ValueHandlerType.ZeroOrNonzero && handler.type != ValueHandlerType.LeValueOrGtValue)
        {
            return null;
        }
        return new(param, handler, luckDependance);
    }

    public static float? LuckChanceMod(float chance, LuckDependance dep, float luck, NodeRollParams rollParams)
    {
        switch (dep)
        {
            case LuckDependance.None:
                return chance;

            case LuckDependance.GoodLuck:
                rollParams.dependsOnLuck = true;
                return chance * SpawnAnalyzer.PredictLuckChanceMod(luck);

            case LuckDependance.BadLuck:
                rollParams.dependsOnLuck = true;
                return chance * SpawnAnalyzer.PredictLuckChanceMod(-luck);

            case LuckDependance.OnlyBadLuck:
                rollParams.dependsOnLuck = true;
                if (luck < 0)
                    return chance * SpawnAnalyzer.PredictLuckChanceMod(-luck);
                return chance;

            case LuckDependance.BadLuckExtreme:
                rollParams.dependsOnLuck = true;
                return chance * SpawnAnalyzer.PredictBadLuckExtremeChanceMod(luck);

            case LuckDependance.OnlyBadLuckExtreme:
                rollParams.dependsOnLuck = true;
                if (luck >= 0)
                    return null;
                return chance * SpawnAnalyzer.PredictBadLuckExtremeChanceMod(luck);
        }
        ;

        return chance;
    }

    public override void NodeHit(SpawnSimulationContext context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        int value = Math.Max(1, this.param.Provide(param, rollParams));

        switch (handler.type)
        {
            case ValueHandlerType.ZeroOrNonzero:

                float hitChance = 1f / value;

                float? tryhitChance = LuckChanceMod(hitChance, luckDependance, context.spawner.luck, rollParams);

                if (tryhitChance is null)
                {
                    branches = [
                        new BranchInfo() {
                            chance = 1f,
                            returnValue = -1,
                        },
                    ];
                }
                else
                {
                    branches = [
                        new BranchInfo() {
                            chance = tryhitChance.Value,
                            returnValue = 0,
                        },
                        new BranchInfo() {
                            chance = 1f - tryhitChance.Value,
                            returnValue = 1,
                        },
                    ];
                }
                return;

            case ValueHandlerType.LeValueOrGtValue:

                hitChance = (float)(handler.value + 1) / value;

                tryhitChance = LuckChanceMod(hitChance, luckDependance, context.spawner.luck, rollParams);

                if (tryhitChance is null)
                {
                    branches = [
                        new BranchInfo() {
                            chance = 1f,
                            returnValue = -1,
                        },
                    ];
                }
                else
                {
                    branches = [
                        new BranchInfo() {
                            chance = tryhitChance.Value,
                            returnValue = handler.value,
                        },
                        new BranchInfo() {
                            chance = 1f - tryhitChance.Value,
                            returnValue = handler.value+1,
                        },
                    ];
                }
                return;

            case ValueHandlerType.AllUnique:
                branches = new BranchInfo[Math.Max(1, value)];
                float chance = 1f / branches.Length;

                for (int i = 0; i < branches.Length; i++)
                {
                    branches[i] = new()
                    {
                        chance = chance,
                        returnValue = i,
                    };
                }

                return;

            default:
                throw new NotImplementedException($"OneParamRandomNextNode value handler {handler}");
        }
    }
}

class TwoParamRandomNextNode : SimulationNode
{
    readonly ParamProvider<(int, int)> param;

    readonly ValueHandler handler;

    private TwoParamRandomNextNode(ParamProvider<(int, int)> param, ValueHandler handler)
    {
        this.param = param;
        this.handler = handler;
    }

    public static TwoParamRandomNextNode? Build(ParamProvider<(int, int)> param, ValueHandler handler)
    {
        if (handler.type != ValueHandlerType.AllUnique)
        {
            return null;
        }

        return new(param, handler);
    }

    public override void NodeHit(SpawnSimulationContext _context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        var (start, end) = this.param.Provide(param, rollParams);

        end = Math.Max(end, start + 1);

        switch (handler.type)
        {
            case ValueHandlerType.AllUnique:
                branches = new BranchInfo[end - start];
                float chance = 1f / branches.Length;

                for (int i = 0; i < branches.Length; i++)
                {
                    branches[i] = new()
                    {
                        chance = chance,
                        returnValue = i + start,
                    };
                }

                return;

            default:
                throw new InvalidOperationException($"TwoParamRandomNextNode is incompatible with handler {handler}");
        }
    }
}

class SelectRandomNode : SimulationNode
{
    readonly ValueHandler handler;

    private SelectRandomNode(ValueHandler handler)
    {
        this.handler = handler;
    }

    public static SelectRandomNode? Build(ValueHandler handler)
    {
        if (handler.type != ValueHandlerType.AllUnique)
        {
            return null;
        }

        return new(handler);
    }

    public static bool SupportsArrayElementType(Type type)
    {
        return type == typeof(int) || type == typeof(short);
    }

    static IEnumerable<int> GetIntEnumerable(object param, out int length)
    {
        if (param is int[] intArray)
        {
            length = intArray.Length;
            return intArray;
        }
        else if (param is short[] shortArray)
        {
            length = shortArray.Length;
            return shortArray.Select(s => (int)s);
        }

        throw new ArgumentException($"Don't know how to enumerate ints from {param.GetType()}");
    }

    public override void NodeHit(SpawnSimulationContext _context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        IEnumerable<int> values = GetIntEnumerable(param, out int length);

        switch (handler.type)
        {
            case ValueHandlerType.AllUnique:
                branches = new BranchInfo[length];
                float chance = 1f / branches.Length;

                int i = 0;
                foreach (int value in values)
                {
                    branches[i] = new()
                    {
                        chance = chance,
                        returnValue = value,
                    };
                    i++;
                }

                return;

            default:
                throw new InvalidOperationException($"SelectRandomNode is incompatible with handler {handler}");
        }
    }
}

class RandomDragonflyTypeNode : SimulationNode
{
    readonly ParamProvider<int> param;

    public RandomDragonflyTypeNode(ParamProvider<int> param)
    {
        this.param = param;
    }

    public override void NodeHit(SpawnSimulationContext context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        int tileType = this.param.Provide(param, rollParams);

        int[] types;

        if (tileType == TileID.Sand)
        {
            types = [595, 598, 600];
        }
        else
        {
            types = [596, 597, 599];
        }

        branches = new BranchInfo[types.Length];
        float chance = 1f / branches.Length;

        for (int i = 0; i < branches.Length; i++)
        {
            branches[i] = new()
            {
                chance = chance,
                returnValue = types[i]
            };
        }
    }
}

enum LuckDependance
{
    None,
    GoodLuck,
    BadLuck,
    OnlyBadLuck,
    BadLuckExtreme,
    OnlyBadLuckExtreme,
}

enum EqualityType
{
    Lt,
    Le,
    Gt,
    Ge
}