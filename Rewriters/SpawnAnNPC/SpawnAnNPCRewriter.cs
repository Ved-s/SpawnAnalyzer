using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.Utilities;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

public class SpawnAnNPCRewriter
{
    public static SpawnAnNPCRewriteData RewriteMethod(MethodInfo? methodOverride = null)
    {
        MethodInfo method = methodOverride ?? typeof(NPC.Spawner).GetMethod("SpawnAnNPC",
            BindingFlags.Instance | BindingFlags.Public, null,
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(int),
            ],
            null
        );

        DynamicMethodDefinition dmd = new(method);

        List<SimulationNodeInfo> nodes = [];

        ILContext il = new(dmd.Definition);

        StateType ls = null!;

        il.Invoke((il) => RewriteMethodInternal(il, nodes, out ls));

        var stack = StackAnalyzer.Analyze(il);
        il.FancyPrintout(stack.instructions);

        Console.WriteLine("Rewrite OK");
        Console.WriteLine();

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo newMethod = dmd.Generate();

        var dg = newMethod.CreateDelegate<RewrittenSpawnAnNPC>();

        return new SpawnAnNPCRewriteData(dg, nodes.ToArray(), ls);
    }

    static void RewriteMethodInternal(ILContext il, List<SimulationNodeInfo> nodes, out StateType localStateType)
    {
        ParameterDefinition entryParam = new("startFromNode", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(int?)));
        ParameterDefinition contextParam = new("context", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(SpawnSimulationContext)));

        VariableDefinition stopVar = new(il.Import(typeof(bool)));
        VariableDefinition randomParamVar = new(il.Import(typeof(object)));
        VariableDefinition stackStateVar = new(il.Import(typeof(object)));
        VariableDefinition tempIntVar = new(il.Import(typeof(int)));

        StackAnalysis stack = StackAnalyzer.Analyze(il);

        il.FancyPrintout(stack.instructions);

        ILCursor c = new(il);

        ILLabel mainEntryLabel = il.DefineLabel();
        List<ILLabel> entryJumps = [];

        RandomCallRewriter randomRewriter = new(nodes, contextParam, stopVar, randomParamVar, stackStateVar, tempIntVar, entryJumps);

        randomRewriter.RewriteRandomCalls(c, stack);

        c.Index = 0;

        if (c.TryGotoNext(
            x => x.MatchLdsfld<Main>("rand")
        ))
        {
            Console.WriteLine($"Found Main.rand reference at IL_{c.Next!.Offset:x4}");
            Environment.Exit(1);
        }

        c.Index = 0;

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

            if (SpawnAnalyzer.MatchInstructions(il, c.Index - 6, out _,
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
                Console.WriteLine($"\x1b[1mUnsupported spawn start at IL_{c.Next!.Offset:x4}:\x1b[0m");

                for (int i = Math.Max(0, c.Index - 10); i <= c.Index; i++)
                {
                    AssemblyPrint.Print(c.Instrs[i]);
                    Console.WriteLine();
                }

                Environment.Exit(1);
            }

            if (SpawnAnalyzer.MatchInstructions(il, c.Index, out _,
                x => x.MatchPop(),
                x => x.MatchRet()
            ))
            {
                c.Remove();
            }
            else if (SpawnAnalyzer.MatchInstructions(il, c.Index, out _,
                x => x.MatchPop(),
                x => x.MatchLdloc(out _),
                x => x.MatchCallOrCallvirt(out MethodReference? mr) && mr.Name == "Clear",
                x => x.MatchRet()
            ))
            {
                c.RemoveRange(3);
            }
            else
            {
                Console.WriteLine($"\x1b[1mUnsupported spawn end at IL_{c.Next!.Offset:x4}:\x1b[0m");

                for (int i = c.Index; i < Math.Min(c.Index + 10, c.Instrs.Count); i++)
                {
                    AssemblyPrint.Print(c.Instrs[i]);
                    Console.WriteLine();
                }

                Environment.Exit(1);
            }
        }

        c.Index = 0;

        int arg = 0;
        while (c.TryGotoNext(
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
                throw new NotImplementedException();
            }

            Instruction oldInstruction = c.Next!;
            c.Emit(OpCodes.Ldarg, contextParam);
            il.RetargetLabels(oldInstruction, c.Prev);

            c.Emit<SpawnSimulationContext>(OpCodes.Ldfld, field);

            c.Next = oldInstruction;
            c.Remove();
        }

        localStateType = LocalStateInfo.RewriteLocalState(il, contextParam);

        c.Index = 0;

        c.Emit(OpCodes.Ldnull);
        c.Emit(OpCodes.Stloc, randomParamVar);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_HasValue");
        c.Emit(OpCodes.Brfalse, mainEntryLabel);

        c.Emit(OpCodes.Ldarga, entryParam);
        c.Emit<int?>(OpCodes.Call, "get_Value");
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
