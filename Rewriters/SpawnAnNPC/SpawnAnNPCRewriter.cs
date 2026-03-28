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

namespace SpawnAnalyzer.Rewriters.SpawnAnNPC;

public class SpawnAnNPCRewriter
{

    static readonly OpCode[] StelemOpcodes = [
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

    static readonly OpCode[] StindOpcodes = [
        OpCodes.Stind_I,
        OpCodes.Stind_I1,
        OpCodes.Stind_I2,
        OpCodes.Stind_I4,
        OpCodes.Stind_I8,
        OpCodes.Stind_R4,
        OpCodes.Stind_R8,
        OpCodes.Stind_Ref,
    ];

    static readonly OpCode[] OutsideWritingOpcodes = [
        OpCodes.Stfld,
        OpCodes.Stsfld,
        OpCodes.Stobj,
        OpCodes.Call,
        OpCodes.Calli,
        OpCodes.Callvirt,

        ..StindOpcodes,
        ..StelemOpcodes
    ];
    public static SpawnAnNPCRewriteData RewriteMethod(MethodInfo? methodOverride = null, bool allowUnknownPatterns = true, bool printoutAfter = false)
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

        bool possiblyNonDeterministic = false;

        il.Invoke((il) => RewriteMethodInternal(il, nodes, out ls, allowUnknownPatterns, out nodeSwitchIndex, out possiblyNonDeterministic));

        if (il.Instrs[nodeSwitchIndex].Operand is Instruction[] nodeInstrs)
        {
            for (int i = 0; i < nodeInstrs.Length; i++)
            {
                nodes[i].Offset = nodeInstrs[i].Offset;
            }
        }

        if (printoutAfter)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                Console.WriteLine($"nodes[{i}] = {node.node.GetType().Name} at IL_{node.Offset:x4}");
            }

            StackAnalysis? stack = null;
            Exception? stackException = null;
            try
            {
                stack = StackAnalyzer.Analyze(il);
            }
            catch (Exception e)
            {
                stackException = e;
            }

            il.FancyPrintout(stack?.instructions);

            if (stackException is not null)
            {
                Console.WriteLine($"Stack analysis exception: {stackException}");
            }
        }

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo newMethod = dmd.Generate();

        var dg = newMethod.CreateDelegate<RewrittenSpawnAnNPC>();

        return new SpawnAnNPCRewriteData(dg, nodes.ToArray(), ls, possiblyNonDeterministic);
    }

    static void RewriteMethodInternal(
        ILContext il,
        List<SimulationNodeInfo> nodes,
        out StateType localStateType,
        bool allowUnknownPatterns,
        out int nodeSwitchIndex,
        out bool possiblyNonDeterministic
    )
    {
        ParameterDefinition entryParam = new("startFromNode", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(int?)));
        ParameterDefinition contextParam = new("context", Mono.Cecil.ParameterAttributes.None, il.Import(typeof(SpawnSimulationContext)));

        VariableDefinition stopVar = new(il.Import(typeof(bool)));
        VariableDefinition randomParamVar = new(il.Import(typeof(object)));
        VariableDefinition stackStateVar = new(il.Import(typeof(object)));
        VariableDefinition tempIntVar = new(il.Import(typeof(int)));
        VariableDefinition tempNullBoolVar = new(il.Import(typeof(bool?)));

        List<Instruction> safeInstructions = new();

        ReplaceGetZombieSetings(il, safeInstructions);

        InlineCalls(il);
        il.Instrs.FillWithFakeILOffsets();

        StackAnalysis stack = StackAnalyzer.Analyze(il);


        ILLabel mainEntryLabel = il.DefineLabel();
        List<ILLabel> entryJumps = [];

        HashSet<FieldInfo> allowFields = new();
        HashSet<MethodInfo> allowMethods = [

            Utils.GetMethodOrThrow<SpawnSimulationContext>("NodeHit"),
            Utils.GetMethodOrThrow<SpawnSimulationContext>("ExitNodeHit"),
            Utils.GetMethodOrThrow<SpawnSimulationContext>("GetLastNodeStackStateClone"),
            Utils.GetMethodOrThrow<SpawnSimulationContext>("GetCurrentTimelineState"),

            Utils.GetMethodOrThrow<NPC>("AnyNPCs"),
            Utils.GetMethodOrThrow<NPC>("CountNPCS"),
            Utils.GetMethodOrThrow<NPC>("AnyDanger"),

            Utils.GetMethodOrThrow<WorldGen>("SolidTile", [typeof(int), typeof(int), typeof(bool)]),
            Utils.GetMethodOrThrow<Collision>("SolidTiles", [typeof(int), typeof(int), typeof(int), typeof(int)]),

            Utils.GetMethodOrThrow(typeof(RuntimeHelpers), "InitializeArray", [typeof(Array), typeof(RuntimeFieldHandle)]),
        ];

        NodeRewriter nodeRewriter = new(
            nodes, contextParam,
            stopVar, randomParamVar, stackStateVar, tempIntVar, tempNullBoolVar,
            entryJumps,
            allowFields, allowMethods,
            stack
        );

        ILCursor c = new(il);

        nodeRewriter.RewriteNodes(c, allowUnknownPatterns);

        c.Index = 0;

        if (c.TryGotoNext(
            x => x.MatchLdsfld<Main>("rand")
        ))
        {
            throw new Exception($"Found Main.rand reference at IL_{c.Next!.Offset:x4}, should have none left");
        }

        RewriteSpawnNPCCalls(c, contextParam, stack);
        RewriteOldArgAccessors(c, contextParam);
        possiblyNonDeterministic = !VerifyNoSideEffects(c, allowFields, allowMethods, safeInstructions, stack, false);

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
        il.Body.Variables.Add(tempNullBoolVar);
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

    static bool VerifyNoSideEffects(ILCursor c, IEnumerable<FieldInfo> allowFields, IEnumerable<MethodBase> allowMethods, IEnumerable<Instruction> allowInstructions, StackAnalysis? stack, bool nested)
    {
        int sideEffects = 0;

        c.Index = 0;

        while (c.TryGotoNext(
            x => OutsideWritingOpcodes.Contains(x.OpCode)
        ))
        {
            if (allowInstructions.Contains(c.Next!)) {
                continue;
            }
            if (c.Next!.Operand is MethodReference method)
            {
                MethodBase resolved = method.ResolveReflection();

                Type? declaringType = resolved.DeclaringType;
                if (declaringType is not null)
                {
                    if (
                        declaringType == typeof(Math)
                     || declaringType.FullName == "System.MathF"
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
                 && resolved.DeclaringType.GetProperty(method.Name.Substring(4), (BindingFlags)(-1)) is not null
                )
                {
                    continue;
                }

                DynamicMethodDefinition dmd = new(resolved);
                ILContext ilc = new(dmd.Definition);

                if (VerifyNoSideEffects(new(ilc), allowFields, allowMethods, [], null, true))
                {
                    continue;
                }
            }

            if (c.Next!.Operand is FieldReference field && allowFields.Any(field.Is))
            {
                continue;
            }

            if (StelemOpcodes.Contains(c.Next.OpCode) && stack is not null)
            {
                InstructionStackInfo? info = stack.LookupInstruction(c.Next, out _);
                if (info is not null)
                {
                    StackValue inputArray = info.inValues[info.inValues.Count - 3];
                    if (inputArray.producedBy.Count == 1)
                    {
                        Instruction producer = inputArray.producedBy[0];
                        bool ok = false;
                        while (true)
                        {
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

                                StackValue inputValue = dupInfo.inValues[dupInfo.inValues.Count - 1];
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

            if (nested)
                return false;

            sideEffects++;

            Console.Write($"Found side-effect instruction [{c.Index:00000}] ");
            AssemblyPrint.Print(c.Next);
            Console.WriteLine();
        }

        return sideEffects == 0;
    }

    static void InlineCalls(ILContext il)
    {
        static bool IsATestInlineMethod(MethodReference method)
        {
            if (method.DeclaringType is null)
                return false;

            if (!method.DeclaringType.Is(typeof(TestMethods)))
                return false;

            MethodBase resolved = method.ResolveReflection();

            return resolved.GetCustomAttribute<TestMethods.TestInlineAttribute>() is not null;
        }
        MethodBase[] inlineMethodsPass1 = [
            Utils.GetMethodOrThrow<NPC.Spawner>("GetBasicSlimeToSpawn"),
            Utils.GetMethodOrThrow<NPC.Spawner>("CheckToSpawnSpider"),
            // Utils.GetMethodOrThrow<NPC>("FindCattailTop"),
        ];

        CallInliner.InlineAllCalls(il, m => inlineMethodsPass1.Any(m.Is) || IsATestInlineMethod(m));

        MethodBase[] inlineMethodsPass2 = [
            Utils.GetMethodOrThrow<NPC.Spawner>("GetBasicSlimeToSpawn_ChanceToBeHolidaySlime"),
        ];

        CallInliner.InlineAllCalls(il, m => inlineMethodsPass2.Any(m.Is));
    }

    static void ReplaceGetZombieSetings(ILContext il, List<Instruction> safeInstructions)
    {
        ILCursor c = new(il);

        /*
            IL_004C: ldarg.0
            IL_004D: ldloca.s  zombieStyle
            IL_004F: ldloca.s  spawnArmedZombies
            IL_0051: ldloca.s  torchZombieChance
            IL_0053: ldloca.s  maggotZombieChance
            IL_0055: call      instance void Terraria.NPC/Spawner::GetZombieSettings(int32&, bool&, int32&, int32&)
        */

        int zombieStyle = 0;
        int spawnArmedZombies = 0;
        int torchZombieChance = 0;
        int maggotZombieChance = 0;

        if (!c.TryGotoNext(
            x => x.MatchLdarg(0),
            x => x.MatchLdloca(out zombieStyle),
            x => x.MatchLdloca(out spawnArmedZombies),
            x => x.MatchLdloca(out torchZombieChance),
            x => x.MatchLdloca(out maggotZombieChance),
            x => x.MatchCall<NPC.Spawner>("GetZombieSettings")
        ))
        {
            Console.WriteLine("Warning! ReplaceGetZombieSetings fail in matching callsite");
            return;
        }

        Instruction callStart = c.Next!;

        int zombieStyleRefLoads = 0;
        int zombieStyleLoads = 0;
        foreach (Instruction instr in il.Instrs)
        {
            if (instr.MatchLdloc(zombieStyle))
            {
                zombieStyleLoads++;
            }
            else if (instr.MatchLdloca(zombieStyle))
            {
                zombieStyleRefLoads++;
            }
        }

        if (zombieStyleRefLoads != 1 || zombieStyleLoads != 3)
        {
            Console.WriteLine("Warning! ReplaceGetZombieSetings fail, unexpected usage of zombieStyle");
            return;
        }

        DynamicMethodDefinition methodDmd = new(Utils.GetMethodOrThrow<NPC.Spawner>("GetZombieSettings"));

        ILContext dctx = new(methodDmd.Definition);
        ILCursor dc = new(dctx);

        // cut this out from GetZombieSettings and paste into main method at specific location
        /*
            zombieStyle = Main.rand.Next(7);
            if (WorldGen.Skyblock.lowTiles && !NPC.DownedAnyPreHardmodeBoss && zombieStyle != 4 && zombieStyle != 5 && Main.rand.Next(3) == 0)
            {
                zombieStyle = ((Main.rand.Next(3) == 0) ? 4 : 5);
            }

            IL_000F: -ldarg.1
            IL_0010:  ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0015:  ldc.i4.7
            IL_0016:  callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_001B: -stind.i4
                     +starg.1

            IL_001C:  ldsfld    bool Terraria.WorldGen/Skyblock::lowTiles
            IL_0021: -brfalse.s IL_0054
                     +brfalse   end

            IL_0023:  call      bool Terraria.NPC::get_DownedAnyPreHardmodeBoss()
            IL_0028: -brtrue.s  IL_0054
                     +brtrue   end

            IL_002A:  ldarg.1
            IL_002B: -ldind.i4
            IL_002C:  ldc.i4.4
            IL_002D: -beq.s     IL_0054
                     +beq.s     end

            IL_002F:  ldarg.1
            IL_0030: -ldind.i4
            IL_0031:  ldc.i4.5
            IL_0032: -beq.s     IL_0054
                     +beq.s     end

            IL_0034:  ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0039:  ldc.i4.3
            IL_003A:  callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_003F: -brtrue.s  IL_0054
                     +brtrue   end

            IL_0041: -ldarg.1
            IL_0042:  ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0047:  ldc.i4.3
            IL_0048:  callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_004D:  brfalse.s IL_0052

            IL_004F:  ldc.i4.5
            IL_0050:  br.s      IL_0053

            IL_0052:  ldc.i4.4

            IL_0053: -stind.i4
                     +starg.1

                end:
        */

        Instruction oldEnd = null!;

        Func<Instruction, bool>[] matchers = [
            /* 0  */ x=>x.MatchLdarg(1),
            /* 1  */ x=>x.MatchLdsfld<Main>("rand"),
            /* 2  */ x=>x.MatchLdcI4(out _),
            /* 3  */ x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            /* 4  */ x=>x.MatchStindI4(),

            /* 5  */ x=>x.MatchLdsfld(typeof(WorldGen.Skyblock), "lowTiles"),
            /* 6  */ x=>{
                bool b = x.OpCode == OpCodes.Brfalse | x.OpCode == OpCodes.Brfalse_S;
                if (b) oldEnd = (Instruction)x.Operand;
                return b;
            },

            /* 7  */ x=>x.MatchCall<NPC>("get_DownedAnyPreHardmodeBoss"),
            /* 8  */ x=>x.OpCode == OpCodes.Brtrue | x.OpCode == OpCodes.Brtrue_S,

            /* 9  */ x=>x.MatchLdarg(1),
            /* 10 */ x=>x.MatchLdindI4(),
            /* 11 */ x=>x.MatchLdcI4(out _),
            /* 12 */ x=>x.OpCode == OpCodes.Beq | x.OpCode == OpCodes.Beq_S,

            /* 13 */ x=>x.MatchLdarg(1),
            /* 14 */ x=>x.MatchLdindI4(),
            /* 15 */ x=>x.MatchLdcI4(out _),
            /* 16 */ x=>x.OpCode == OpCodes.Beq | x.OpCode == OpCodes.Beq_S,

            /* 17 */ x=>x.MatchLdsfld<Main>("rand"),
            /* 18 */ x=>x.MatchLdcI4(out _),
            /* 19 */ x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            /* 20 */ x=>x.OpCode == OpCodes.Brtrue | x.OpCode == OpCodes.Brtrue_S,

            /* 21 */ x=>x.MatchLdarg(1),
            /* 22 */ x=>x.MatchLdsfld<Main>("rand"),
            /* 23 */ x=>x.MatchLdcI4(out _),
            /* 24 */ x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            /* 25 */ x=>x.OpCode == OpCodes.Brfalse | x.OpCode == OpCodes.Brfalse_S,

            /* 26 */ x=>x.MatchLdcI4(out _),
            /* 27 */ x=>x.OpCode == OpCodes.Br | x.OpCode == OpCodes.Br_S,

            /* 28 */ x=>x.MatchLdcI4(out _),

            /* 29 */ x=>x.MatchStindI4(),
        ];

        if (!dc.TryGotoNext(matchers))
        {
            Console.WriteLine("Warning! ReplaceGetZombieSetings fail in matching random bit");
            return;
        }

        List<Instruction> instructions = new(matchers.Length);

        int start = dc.Index;
        for (int i = start; i < start + matchers.Length; i++)
        {
            int index = i - start;
            if (index == 0 || index == 10 || index == 14 || index == 21)
                continue;

            Instruction instr = dc.Instrs[i];

            if (index == 4 || index == 29)
            {
                instr.OpCode = OpCodes.Starg;
                instr.Operand = dctx.Method.Parameters[1];
            }

            instructions.Add(instr);
        }

        dc.RemoveRange(matchers.Length);

        // verify no random calls remain in the method
        dc.Index = 0;
        if (dc.TryGotoNext(
            x => x.MatchLdsfld<Main>("rand")
        )) {
            Console.WriteLine("Warning! ReplaceGetZombieSetings fail, there's more unknown randomness in GetZombieSetings");
            return;
        }

        // search for the place to inject into
        /*
            <inject here>

            end:

            IL_B766: ldloc.2   spawnArmedZombies
            IL_B767: brfalse   IL_B81A

            IL_B76C: ldloc.1   zombieStyle
            IL_B76D: ldc.i4.1
            IL_B76E: beq       IL_B81A

            IL_B773: call      bool Terraria.Main::get_expertMode()
            IL_B778: brfalse   IL_B81A
        */

        if (!c.TryGotoNext(
            MoveType.AfterLabel,
            x => x.MatchLdloc(spawnArmedZombies),
            x => x.MatchBrfalse(out _),

            x => x.MatchLdloc(zombieStyle),
            x => x.MatchLdcI4(out _),
            x => x.MatchBeq(out _),

            x => x.MatchCall<Main>("get_expertMode"),
            x => x.MatchBrfalse(out _)
        ))
        {
            Console.WriteLine("Warning! ReplaceGetZombieSetings fail in matching injection spot");
            return;
        }

        foreach (Instruction instr in dc.Instrs) {
            if (StindOpcodes.Contains(instr.OpCode)) {
                safeInstructions.Add(instr);
            }
        }

        Instruction newEnd = c.Next!;

        c.Emit(OpCodes.Nop);
        Instruction nop = c.Prev;

        foreach (Instruction instr in instructions)
        {
            if (instr.Operand == oldEnd)
            {
                instr.Operand = newEnd;
            }
        }

        CallInliner.InlineMethodBody(il, c.Index, [], instructions, [new InlineParameter.Null(), new InlineParameter.Local(zombieStyle)], allowStarg: true);

        c.Goto(nop, MoveType.AfterLabel);
        c.Remove();

        c.Goto(callStart);

        /*
         -> IL_004C: ldarg.0
            IL_004D: ldloca.s  zombieStyle
            IL_004F: ldloca.s  spawnArmedZombies
            IL_0051: ldloca.s  torchZombieChance
            IL_0053: ldloca.s  maggotZombieChance
            IL_0055: call      instance void Terraria.NPC/Spawner::GetZombieSettings(int32&, bool&, int32&, int32&)
        */

        InlineParameter[] methodParams = [
            new InlineParameter.Argument(0),
            new InlineParameter.LocalRef(zombieStyle),
            new InlineParameter.LocalRef(spawnArmedZombies),
            new InlineParameter.LocalRef(torchZombieChance),
            new InlineParameter.LocalRef(maggotZombieChance),
        ];

        CallInliner.InlineMethodDefinition(il, c.Index + 6, methodDmd.Definition, methodParams);

        c.RemoveRange(6);

        // il.Instrs.FillWithFakeILOffsets();
        // il.FancyPrintout();

        // Environment.Exit(1);
    }
}

public delegate void RewrittenSpawnAnNPC(int? startFromRandomNode, SpawnSimulationContext ctx);

public class SpawnAnNPCRewriteData
{
    public RewrittenSpawnAnNPC Method;

    public SimulationNodeInfo[] Nodes;

    public StateType LocalStateType;

    public bool PossiblyNonDeterministic;

    public SpawnAnNPCRewriteData(RewrittenSpawnAnNPC method, SimulationNodeInfo[] nodes, StateType localStateType, bool possiblyNonDeterministic)
    {
        Method = method;
        Nodes = nodes;
        LocalStateType = localStateType;
        PossiblyNonDeterministic = possiblyNonDeterministic;
    }
}
