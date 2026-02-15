using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.Utilities;

using OpCode = Mono.Cecil.Cil.OpCode;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

public class SpawnAnNPCRewriter
{

    static OpCode[] StelemOpcodes = [
        OpCodes.Stelem_Any,
        OpCodes.Stelem_I,
        OpCodes.Stelem_I1,
        OpCodes.Stelem_I2,
        OpCodes.Stelem_I4,
        OpCodes.Stelem_I8,
        OpCodes.Stelem_R4,
        OpCodes.Stelem_R8,
        OpCodes.Stelem_Ref,
    ];

    static OpCode[] OutsideWritingOpcodes = [
        OpCodes.Stfld,
        OpCodes.Stsfld,
        OpCodes.Stind_I,
        OpCodes.Stind_I1,
        OpCodes.Stind_I2,
        OpCodes.Stind_I4,
        OpCodes.Stind_I8,
        OpCodes.Stind_R4,
        OpCodes.Stind_R8,
        OpCodes.Stind_Ref,
        OpCodes.Stobj,
        OpCodes.Call,
        OpCodes.Calli,
        OpCodes.Callvirt,

        ..StelemOpcodes
    ];
    public static SpawnAnNPCRewriteData RewriteMethod(MethodInfo? methodOverride = null, bool allowUnknownPatterns = true)
    {
        MethodInfo method = methodOverride ?? Utils.GetMethodOrThrow<NPC.Spawner>("SpawnAnNPC",
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(int),
            ]
        );

        DynamicMethodDefinition dmd = new(method);

        List<SimulationNodeInfo> nodes = [];

        ILContext il = new(dmd.Definition);

        StateType ls = null!;

        int nodeSwitchIndex = 0;

        il.Invoke((il) => RewriteMethodInternal(il, nodes, out ls, allowUnknownPatterns, out nodeSwitchIndex));

        if (il.Instrs[nodeSwitchIndex].Operand is Instruction[] nodeInstrs)
        {
            for (int i = 0; i < nodeInstrs.Length; i++)
            {
                nodes[i].Offset = nodeInstrs[i].Offset;
            }
        }

        // var stack = StackAnalyzer.Analyze(il);
        // il.FancyPrintout(stack.instructions);

        // Console.WriteLine("Rewrite OK");
        // Console.WriteLine();

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo newMethod = dmd.Generate();

        var dg = newMethod.CreateDelegate<RewrittenSpawnAnNPC>();

        return new SpawnAnNPCRewriteData(dg, nodes.ToArray(), ls);
    }

    static void RewriteMethodInternal(ILContext il, List<SimulationNodeInfo> nodes, out StateType localStateType, bool allowUnknownPatterns, out int nodeSwitchIndex)
    {
        ParameterDefinition entryParam = new("startFromNode", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(int?)));
        ParameterDefinition contextParam = new("context", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(SpawnSimulationContext)));

        VariableDefinition stopVar = new(il.Import(typeof(bool)));
        VariableDefinition randomParamVar = new(il.Import(typeof(object)));
        VariableDefinition stackStateVar = new(il.Import(typeof(object)));
        VariableDefinition tempIntVar = new(il.Import(typeof(int)));

        StackAnalysis stack = StackAnalyzer.Analyze(il);

        ILCursor c = new(il);

        ILLabel mainEntryLabel = il.DefineLabel();
        List<ILLabel> entryJumps = [];

        HashSet<FieldInfo> allowFields = new();
        HashSet<MethodInfo> allowMethods = [

            Utils.GetMethodOrThrow<SpawnSimulationContext>("NodeHit"),
            Utils.GetMethodOrThrow<SpawnSimulationContext>("ExitNodeHit"),
            Utils.GetMethodOrThrow<SpawnSimulationContext>("GetLastNodeStackStateClone"),

            Utils.GetMethodOrThrow<NPC>("AnyNPCs"),
            Utils.GetMethodOrThrow<NPC>("CountNPCS"),
            Utils.GetMethodOrThrow<NPC>("AnyDanger"),

            Utils.GetMethodOrThrow<WorldGen>("SolidTile", [typeof(int), typeof(int), typeof(bool)]),
            Utils.GetMethodOrThrow<Collision>("SolidTiles", [typeof(int), typeof(int), typeof(int), typeof(int)]),

            Utils.GetMethodOrThrow(typeof(RuntimeHelpers), "InitializeArray"),
        ];

        RandomCallRewriter randomRewriter = new(nodes, contextParam, stopVar, randomParamVar, stackStateVar, tempIntVar, entryJumps, allowFields, allowMethods);

        randomRewriter.RewriteRandomCalls(c, stack, allowUnknownPatterns);

        c.Index = 0;

        if (c.TryGotoNext(
            x => x.MatchLdsfld<Main>("rand")
        ))
        {
            throw new Exception($"Found Main.rand reference at IL_{c.Next!.Offset:x4}, should have none left");
        }

        RewriteSpawnNPCCalls(c, contextParam, stack);
        RewriteOldArgAccessors(c, contextParam);

        il.FancyPrintout();

        VerifyNoSideEffects(c, allowFields, allowMethods, stack);

        localStateType = LocalStateInfo.RewriteLocalState(il, contextParam);

        c.Index = 0;

        c.Emit(OpCodes.Ldnull);
        c.Emit(OpCodes.Stloc, randomParamVar);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_HasValue");
        c.Emit(OpCodes.Brfalse, mainEntryLabel);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_Value");
        nodeSwitchIndex = c.Index;

        c.Emit(OpCodes.Switch, entryJumps.ToArray());
        c.Emit(OpCodes.Ret);
        c.MarkLabel(mainEntryLabel);

        il.Method.Parameters.Clear();
        il.Method.Parameters.Add(entryParam);
        il.Method.Parameters.Add(contextParam);

        il.Body.Variables.Add(stopVar);
        il.Body.Variables.Add(randomParamVar);
        il.Body.Variables.Add(stackStateVar);
        il.Body.Variables.Add(tempIntVar);
    }

    static void RewriteSpawnNPCCalls(ILCursor c, ParameterDefinition contextParam, StackAnalysis stack)
    {
        c.Index = 0;

        ulong unknownSpawns = 0;
        ulong knownSpawns = 0;

        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<NPC.Spawner>("SpawnNPC")
        ))
        {
            MethodReference spawnNpcMethod = (MethodReference)c.Next!.Operand;
            InstructionStackInfo stackInfo = stack.LookupInstruction(c.Next, out _)
                ?? throw new InvalidOperationException("Stack analysis out of date or invalid");

            int restParams = spawnNpcMethod.Parameters.Count;
            StackValue value = stackInfo.inValues[stackInfo.inValues.Count - 1 - restParams];

            if (value.producedBy.Count != 1)
                throw new InvalidOperationException($"Invalid this param value source for random call at IL_{c.Next.Offset:x4}");

            Instruction thisLoadInstr = value.producedBy[0];

            if (!thisLoadInstr.MatchLdarg(0))
                throw new InvalidOperationException($"Invalid this param value source (at IL_{thisLoadInstr.Offset:x4}) for random call at IL_{c.Next.Offset:x4}");

            thisLoadInstr.OpCode = OpCodes.Ldarg;
            thisLoadInstr.Operand = contextParam;

            if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 6, out _,
                x => x.MatchLdcI4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcI4(out _),
                _ => true
            ))
            {
                c.Index -= 6;
                c.RemoveRange(7);
                c.Emit<SpawnSimulationContext>(OpCodes.Call, "ExitNodeHit");
            }
            else
            {
                // SpawnAnalyzer.ReportUnknownPattern("spawn start", c.Context, c.Index, 10, 5);
                // unknownSpawns++;
                // continue;
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Remove();
                c.Emit<SpawnSimulationContext>(OpCodes.Call, "ExitNodeHit");
            }

            if (c.Next.MatchPop())
            {
                c.Remove();
            }
            else if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index, out _,
                x => x.MatchDup(),
                x => x.MatchLdfld<NPC>("timeLeft"),
                x => x.MatchLdcI4(out _),
                x => x.MatchMul(),
                x => x.MatchStfld<NPC>("timeLeft")
            ))
            {
                // TODO: register somewhere that spawned NPC has more time
                c.RemoveRange(5);
            }
            else if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index, out _,
                x => x.MatchLdcI4(out _),
                x => x.MatchCallOrCallvirt<NPC>("TargetClosest")
            ))
            {
                c.RemoveRange(2);
            }
            else
            {
                SpawnAnalyzer.ReportUnknownPattern("spawn end", c.Context, c.Index, 5, 10);
                unknownSpawns++;
                continue;
            }

            knownSpawns++;
        }

        if (unknownSpawns > 0)
        {
            ulong totalSpawns = knownSpawns + unknownSpawns;
            double done = (double)knownSpawns / totalSpawns;
            throw new Exception($"{done * 100:0.0}% ({knownSpawns}/{totalSpawns}) of SpawnNPC calls patched");
        }
    }

    static void RewriteOldArgAccessors(ILCursor c, ParameterDefinition contextParam)
    {
        c.Index = 0;

        int arg = 0;
        while (c.TryGotoNext(
            MoveType.AfterLabel,
            x => x.MatchLdarg(out arg)
        ))
        {
            if (arg < 0)
            {
                continue;
            }

            string? field = arg switch
            {
                0 => "spawner",
                1 => "spawnTileX",
                2 => "spawnTileY",
                3 => "spawnTileType",
                4 => "xRange",
                _ => null
            };

            if (field is null)
            {
                if (arg == 5)
                {
                    c.Next!.OpCode = OpCodes.Ldc_I4_0;
                    c.Next!.Operand = null;
                    continue;
                }
                else
                    throw new NotImplementedException($"ldarg {arg} at IL_{c.Next!.Offset:x4}");
            }

            Instruction oldInstruction = c.Next!;

            c.Emit(OpCodes.Ldarg, contextParam);
            c.Emit<SpawnSimulationContext>(OpCodes.Ldfld, field);

            c.Next = oldInstruction;
            c.Remove();
        }
    }

    static void VerifyNoSideEffects(ILCursor c, IEnumerable<FieldInfo> allowFields, IEnumerable<MethodBase> allowMethods, StackAnalysis stack)
    {
        int sideEffects = 0;

        c.Index = 0;

        while (c.TryGotoNext(
            x=>OutsideWritingOpcodes.Contains(x.OpCode)
        ))
        {
            if (c.Next!.Operand is MethodReference method)
            {
                MethodBase resolved = method.ResolveReflection();

                Type? declaringType = resolved.DeclaringType;
                if (declaringType is not null)
                {
                    if (
                        declaringType == typeof(Math) 
                     || declaringType == typeof(MathF)
                     || (declaringType.IsGenericType && declaringType.GetGenericTypeDefinition() == typeof(List<>))
                     || declaringType.IsArray
                    )
                    {
                        continue;
                    }
                }

                if (allowMethods.Any(m => m == resolved))
                {
                    continue;
                }

                if (method.Name.StartsWith("get_") 
                 && resolved.DeclaringType is not null 
                 && resolved.DeclaringType.GetProperty(method.Name[4..], (BindingFlags)(-1)) is not null
                )
                {
                    continue;
                }
            }

            if (c.Next!.Operand is FieldReference field && allowFields.Any(field.Is))
            {
                continue;
            }

            if (StelemOpcodes.Contains(c.Next.OpCode))
            {
                InstructionStackInfo? info = stack.LookupInstruction(c.Next, out _);
                if (info is not null)
                {
                    StackValue inputArray = info.inValues[^3];
                    if (inputArray.producedBy.Count == 1)
                    {
                        Instruction producer = inputArray.producedBy[0];
                        bool ok = false;
                        while (true) {
                            if (c.Instrs.IndexOf(producer) < 0) 
                                break;
                            
                            if (producer.OpCode == OpCodes.Newarr)
                            {
                                ok = true;
                                break;
                            }

                            if (producer.OpCode == OpCodes.Dup)
                            {
                                InstructionStackInfo? dupInfo = stack.LookupInstruction(producer, out _);
                                if (dupInfo is null)
                                    break;
                                
                                StackValue inputValue = dupInfo.inValues[^1];
                                if (inputValue.producedBy.Count != 1)
                                    break;

                                producer = inputValue.producedBy[0];
                                continue;
                            }

                            break;
                        }

                        if (ok)
                            continue;
                    }
                }

            }

            sideEffects++;

            Console.Write($"Found side-effect instruction [{c.Index:00000}] ");
            AssemblyPrint.Print(c.Next);
            Console.WriteLine();
        }


        if (sideEffects > 0)
            throw new Exception($"{sideEffects} instructions with side-effects detected");
    }
}

public delegate void RewrittenSpawnAnNPC(int? startFromRandomNode, SpawnSimulationContext ctx);

public class SpawnAnNPCRewriteData
{
    public RewrittenSpawnAnNPC Method;

    public SimulationNodeInfo[] Nodes;

    public StateType LocalStateType;

    public SpawnAnNPCRewriteData(RewrittenSpawnAnNPC method, SimulationNodeInfo[] nodes, StateType localStateType)
    {
        Method = method;
        Nodes = nodes;
        LocalStateType = localStateType;
    }
}
