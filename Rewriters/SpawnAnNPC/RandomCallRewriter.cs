using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.GameContent;
using Terraria.Utilities;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

class RandomCallRewriter
{
    readonly List<SimulationNode> nodes;
    readonly ParameterDefinition contextParam;
    readonly VariableDefinition stopVar;
    readonly VariableDefinition nodeParamVar;
    readonly List<ILLabel> entryJumps;

    public RandomCallRewriter(
        List<SimulationNode> nodes,
        ParameterDefinition contextParam,
        VariableDefinition stopVar,
        VariableDefinition randomParamVar,
        List<ILLabel> entryJumps
    )
    {
        this.nodes = nodes;
        this.contextParam = contextParam;
        this.stopVar = stopVar;
        this.nodeParamVar = randomParamVar;
        this.entryJumps = entryJumps;
    }

    /*
    public void RewriteRandomCalls(ILCursor c)
    {
        ulong unknownPatterns = 0;
        ulong knownPatterns = 0;

        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<UnifiedRandom>("Next")
              || x.MatchCall("Terraria.Utils", "SelectRandom")
              || x.MatchCallOrCallvirt(out MethodReference? mr) && mr.Name.StartsWith("Roll")
        ))
        {
            MethodReference next = (c.Next!.Operand as MethodReference)!;

            if (next.Name == "Next")
            {
                int denominator = 0;
                if (next.Parameters.Count == 1 && SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 2, out _,
                    x => x.MatchLdsfld<Main>("rand"),
                    x => x.MatchLdcI4(out denominator),
                    _ => true,
                    x => x.MatchBrtrue(out _) || x.MatchBrfalse(out _)
                ))
                {
                    c.Index -= 2;
                    Instruction oldFirstInstruction = c.Next;

                    EmitRandomNode(c, oldFirstInstruction, new FixedChanceTwoBranchRandomNode(1f / denominator), RandomNodeParameterBehavior.AlwaysNull);

                    c.Next = oldFirstInstruction;
                    c.RemoveRange(3);

                    knownPatterns++;
                    continue;
                }

                if (next.Parameters.Count == 1 && SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 2, out _,
                    x => x.MatchLdsfld<Main>("rand"),
                    x => x.MatchLdcI4(out denominator),
                    _ => true,
                    x => x.MatchDup() || x.MatchStloc(out _)
                ))
                {
                    c.Index -= 2;
                    Instruction oldFirstInstruction = c.Next;

                    EmitRandomNode(c, oldFirstInstruction, new FixedValueEqualChanceRangeRandomNode(0, denominator), RandomNodeParameterBehavior.AlwaysNull);

                    c.Next = oldFirstInstruction;
                    c.RemoveRange(3);

                    knownPatterns++;
                    continue;
                }

                // TODO: different return values
                // int value = 0;
                // if (next.Parameters.Count == 1 && SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 2, out _,
                //     x => x.MatchLdsfld<Main>("rand"),
                //     x => x.MatchLdcI4(out denominator),
                //     _ => true,
                //     x => x.MatchLdcI4(out value),
                //     x => x.MatchBle(out _)
                // ))
                // {
                //     c.Index -= 2;
                //     Instruction oldFirstInstruction = c.Next;

                //     float chance = (float)(denominator - value - 1) / denominator;
                //     chance = Math.Min(Math.Max(0, chance), 1);

                //     EmitRandomNode(oldFirstInstruction, new FixedChanceTwoBranchRandomNode(chance), RandomNodeParameterBehavior.AlwaysNull);

                //     c.Next = oldFirstInstruction;
                //     c.RemoveRange(4);

                //     knownPatterns++;
                //     continue;
                // }

                int rangeStart = 0;
                int rangeEnd = 0;

                if (next.Parameters.Count == 2 && SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 3, out _,
                    x => x.MatchLdsfld<Main>("rand"),
                    x => x.MatchLdcI4(out rangeStart),
                    x => x.MatchLdcI4(out rangeEnd),
                    _ => true
                ))
                {
                    c.Index -= 3;
                    Instruction oldFirstInstruction = c.Next;

                    EmitRandomNode(c, oldFirstInstruction, new FixedValueEqualChanceRangeRandomNode(rangeStart, rangeEnd), RandomNodeParameterBehavior.AlwaysNull);

                    c.Next = oldFirstInstruction;
                    c.RemoveRange(4);

                    knownPatterns++;
                    continue;
                }
            }
            else if (next.Name == "SelectRandom")
            {
                TypeReference intType = c.Context.Import(typeof(int));
                int arraySize = 0;
                IMetadataTokenProvider? arrayData = null;
                if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 6, out _,
                    x => x.MatchLdsfld<Main>("rand"),
                    x => x.MatchLdcI4(out arraySize),
                    x => x.MatchNewarr(intType),
                    x => x.MatchDup(),
                    x => x.MatchLdtoken(out arrayData),
                    x => x.MatchCall("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray"),
                    _ => true
                ) && arrayData is FieldReference field)
                {
                    var ints = new int[arraySize];
                    RuntimeHelpers.InitializeArray(ints, field.ResolveReflection().FieldHandle);

                    c.Index -= 6;
                    Instruction oldFirstInstruction = c.Next;

                    EmitRandomNode(c, oldFirstInstruction, new FixedValueEqualChanceIdArrayRandomNode(ints), RandomNodeParameterBehavior.AlwaysNull);

                    c.Next = oldFirstInstruction;
                    c.RemoveRange(7);

                    knownPatterns++;
                    continue;
                }
                else if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 3, out _,
                    x => x.MatchLdsfld<Main>("rand"),
                    x => x.MatchLdloc(out _),
                    x => x.MatchCallOrCallvirt(out _)
                ))
                {
                    c.Index -= 3;
                    c.Next.OpCode = OpCodes.Nop;
                    c.Next.Operand = null;

                    c.Index += 3;
                    c.Remove();

                    c.Emit(OpCodes.Stloc, randomParamVar);

                    EmitRandomNode(c, null, new DynamicValueEqualChanceIdArrayRandomNode(), RandomNodeParameterBehavior.LoadFromParamVar);

                    knownPatterns++;
                    continue;
                }
            }

            Console.WriteLine($"\n\x1b[1mUnsupported random at IL_{c.Next!.Offset:x4}:\x1b[0m");

            for (int i = Math.Max(0, c.Index - 10); i <= Math.Min(c.Index + 5, c.Instrs.Count); i++)
            {
                if (i == c.Index)
                    Console.Write("-> ");
                else
                    Console.Write("   ");
                AssemblyPrint.Print(c.Instrs[i]);
                Console.WriteLine();
            }

            unknownPatterns++;
        }

        if (unknownPatterns > 0)
        {
            double percent = (double)knownPatterns / (knownPatterns + unknownPatterns) * 100;
            Console.WriteLine($"\n{percent:0.0}% ({knownPatterns}/{knownPatterns + unknownPatterns}) of Random calls were patched");
            Environment.Exit(1);
        }
    }
    */

    public void RewriteRandomCalls(ILCursor c, StackAnalysis stack)
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

            c.Goto(c.Instrs.IndexOf(instr) + 1);
            ValueHandlerType? valueHandler = TryCreateValueHandler(c);
            if (valueHandler is null)
            {
                ReportInvalid("random call value handler", c.Context, c.Instrs.IndexOf(instr) + 1, 2, 10);
                c.Goto(c.Instrs.IndexOf(instr) + 1);
                unknownPatterns++;
                continue;
            }

            if (method.Parameters.Count == 1 && method.Parameters[0].ParameterType.Is(typeof(int)))
            {
                c.Goto(c.Instrs.IndexOf(instr) - 1);
                Instruction lastParamInitInstr = c.Next!;

                ParamProvider<int> param = CreateSingleIntProvider(c);

                SimulationNode? node = TryBuildSingleIntSimulationNode(method, param, valueHandler.Value);
                if (node is null)
                {
                    ReportInvalid("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(c.Instrs.IndexOf(instr) + 1);
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
                c.Goto(c.Instrs.IndexOf(instr) - 1);
                Instruction lastParamInitInstr = c.Next!;

                ParamProvider<(int, int)> param = CreateDoubleIntProvider(c);

                SimulationNode? node = TryBuildDoubleIntSimulationNode(method, param, valueHandler.Value);
                if (node is null)
                {
                    ReportInvalid("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(c.Instrs.IndexOf(instr) + 1);
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
                c.Goto(c.Instrs.IndexOf(instr) - 1);
                Instruction lastParamInitInstr = c.Next!;

                ParamProvider<int[]>? param = TryCreateIntArrayProvider(c);
                if (param is null)
                {
                    ReportInvalid("random call parameters", c.Context, c.Instrs.IndexOf(lastParamInitInstr), 10, 2);
                    c.Goto(c.Instrs.IndexOf(instr) + 1);
                    unknownPatterns++;
                    continue;
                }

                SimulationNode? node = SelectRandomNode.Build(param, valueHandler.Value);
                if (node is null)
                {
                    ReportInvalid("random call method", c.Context, c.Instrs.IndexOf(instr), 5, 5);
                    c.Goto(c.Instrs.IndexOf(instr) + 1);
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

            ReportInvalid("random call", c.Context, c.Instrs.IndexOf(instr), 10, 5);
            c.Goto(c.Instrs.IndexOf(instr) + 1);
            unknownPatterns++;
            continue;
        }

        if (unknownPatterns > 0)
        {
            double percent = (double)knownPatterns / (knownPatterns + unknownPatterns) * 100;
            Console.WriteLine($"\n{percent:0.0}% ({knownPatterns}/{knownPatterns + unknownPatterns}) of Random calls were patched");
            Environment.Exit(1);
        }
    }

    private void EmitNode(ILCursor c, SimulationNode node, NodeParameterInputBehavior pb, StackValue[] stackValues)
    {
        ILLabel afterStopHandler = c.DefineLabel();

        ILLabel entryJumpLabel = c.DefineLabel();
        entryJumps.Add(entryJumpLabel);

        if (stackValues.Length > 0)
        {
            throw new NotImplementedException("entry land with stack values");
        }
        else
        {
            c.MarkLabel(entryJumpLabel);
        }

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
        c.Emit<SpawnSimulationContext>(OpCodes.Call, "RandomNodeHit");
        c.Emit(OpCodes.Ldloc, stopVar);
        c.Emit(OpCodes.Brfalse, afterStopHandler);

        // rolledValue
        c.Emit(OpCodes.Pop);

        foreach (var _ in stackValues)
        {
            c.Emit(OpCodes.Pop);
        }
        
        c.Emit(OpCodes.Ret);

        c.MarkLabel(afterStopHandler);

        nodes.Add(node);
    }

    private ParamProvider<int> CreateSingleIntProvider(ILCursor c)
    {
        int staticValue = 0;
        if (c.Index > 0 && SpawnAnalyzer.MatchInstructions(
            c.Context, c.Index, out _,
            x => x.MatchLdcI4(out staticValue)
        ))
        {
            c.Remove();

            return new StaticParamProvider<int>(staticValue);
        }

        c.Index += 1;

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

    private ParamProvider<int[]>? TryCreateIntArrayProvider(ILCursor c)
    {
        TypeReference intType = c.Context.Import(typeof(int));
        int arraySize = 0;
        IMetadataTokenProvider? arrayData = null;
        if (c.Index >= 4 && SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 4, out _,
            x => x.MatchLdcI4(out arraySize),
            x => x.MatchNewarr(intType),
            x => x.MatchDup(),
            x => x.MatchLdtoken(out arrayData),
            x => x.MatchCall("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray")
        ) && arrayData is FieldReference field)
        {
            c.Index -= 4;
            c.RemoveRange(5);

            var ints = new int[arraySize];
            RuntimeHelpers.InitializeArray(ints, field.ResolveReflection().FieldHandle);

            return new StaticParamProvider<int[]>(ints);
        }

        c.Index += 1;
        c.Emit(OpCodes.Stloc, nodeParamVar);

        return new RuntimeParamVarCastParamProvider<int[]>();
    }

    private ValueHandlerType TryCreateValueHandler(ILCursor c)
    {
        if (c.Next!.MatchBrfalse(out _) || c.Next!.MatchBrtrue(out _))
        {
            return ValueHandlerType.SimpleBranch;
        }

        return ValueHandlerType.AllUnique;
    }

    private SimulationNode? TryBuildSingleIntSimulationNode(MethodReference method, ParamProvider<int> param, ValueHandlerType valHandler)
    {
        if (method.Name == "Next")
        {
            return OneParamRandomNextNode.Build(param, valHandler, false);
        }
        if (method.Name == "RollLuck")
        {
            return OneParamRandomNextNode.Build(param, valHandler, true);
        }
        return null;
    }

    private SimulationNode? TryBuildDoubleIntSimulationNode(MethodReference method, ParamProvider<(int, int)> param, ValueHandlerType valHandler)
    {
        if (method.Name == "Next")
        {
            return TwoParamRandomNextNode.Build(param, valHandler);
        }
        return null;
    }

    private void ReportInvalid(string type, ILContext c, int index, int showBefore, int showAfter)
    {
        Console.WriteLine($"\n\x1b[1mUnsupported {type} at IL_{c.Instrs[index].Offset:x4}:\x1b[0m");

        for (int i = Math.Max(0, index - showBefore); i <= Math.Min(index + showAfter, c.Instrs.Count); i++)
        {
            if (i == index)
                Console.Write("-> ");
            else
                Console.Write("   ");
            AssemblyPrint.Print(c.Instrs[i]);
            Console.WriteLine();
        }
    }

    private static object PackTwoIntTupleBoxed(int a, int b)
    {
        return (a, b);
    }
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

enum ValueHandlerType
{
    SimpleBranch,
    AllUnique,
}

// abstract class ValueHandler
// {
//     public abstract bool AllValuesAreUnique { get; }
//     public abstract ValueRange[] GetExpectedValueRanges();
// }

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

// class SimpleBranchValueHandler : ValueHandler
// {
//     public override bool AllValuesAreUnique => false;

//     public override ValueRange[] GetExpectedValueRanges()
//     {
//         return [
//             new(0, 1),
//             new(1, null)
//         ];
//     }
// }

class OneParamRandomNextNode : SimulationNode
{
    readonly ParamProvider<int> param;

    readonly ValueHandlerType handler;

    readonly bool affectedByLuck;

    private OneParamRandomNextNode(ParamProvider<int> param, ValueHandlerType handler, bool affectedByLuck)
    {
        this.param = param;
        this.handler = handler;
        this.affectedByLuck = affectedByLuck;
    }

    public static OneParamRandomNextNode? Build(ParamProvider<int> param, ValueHandlerType handler, bool affectedByLuck)
    {
        if (handler != ValueHandlerType.SimpleBranch && handler != ValueHandlerType.AllUnique)
        {
            return null;
        }
        if (affectedByLuck && handler != ValueHandlerType.SimpleBranch)
        {
            return null;
        }
        return new(param, handler, affectedByLuck);
    }

    public override void NodeHit(SpawnSimulationContext context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        int value = Math.Max(1, this.param.Provide(param, rollParams));

        switch (handler)
        {
            case ValueHandlerType.SimpleBranch:

                float hitChance = 1f / value;

                if (affectedByLuck)
                {
                    rollParams.dependsOnLuck = true;
                    hitChance *= SpawnAnalyzer.PredictLuckChanceMod(context.spawner.luck);
                }

                branches = [
                    new BranchInfo() {
                        chance = hitChance,
                        returnValue = 0,
                    },
                    new BranchInfo() {
                        chance = 1f - hitChance,
                        returnValue = 1,
                    },
                ];
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

    readonly ValueHandlerType handler;

    private TwoParamRandomNextNode(ParamProvider<(int, int)> param, ValueHandlerType handler)
    {
        this.param = param;
        this.handler = handler;
    }

    public static TwoParamRandomNextNode? Build(ParamProvider<(int, int)> param, ValueHandlerType handler)
    {
        if (handler != ValueHandlerType.AllUnique)
        {
            return null;
        }

        return new(param, handler);
    }

    public override void NodeHit(SpawnSimulationContext _context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        var (start, end) = this.param.Provide(param, rollParams);

        end = Math.Max(end, start + 1);

        switch (handler)
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
    readonly ParamProvider<int[]> param;

    readonly ValueHandlerType handler;

    private SelectRandomNode(ParamProvider<int[]> param, ValueHandlerType handler)
    {
        this.param = param;
        this.handler = handler;
    }

    public static SelectRandomNode? Build(ParamProvider<int[]> param, ValueHandlerType handler)
    {
        if (handler != ValueHandlerType.AllUnique)
        {
            return null;
        }

        return new(param, handler);
    }

    public override void NodeHit(SpawnSimulationContext _context, object param, NodeRollParams rollParams, out BranchInfo[] branches)
    {
        int[] values = this.param.Provide(param, rollParams);

        switch (handler)
        {
            case ValueHandlerType.AllUnique:
                branches = new BranchInfo[values.Length];
                float chance = 1f / branches.Length;

                for (int i = 0; i < branches.Length; i++)
                {
                    branches[i] = new()
                    {
                        chance = chance,
                        returnValue = values[i],
                    };
                }

                return;

            default:
                throw new InvalidOperationException($"SelectRandomNode is incompatible with handler {handler}");
        }
    }
}