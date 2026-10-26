using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using SpawnAnalyzer.UI.Tabs;
using Terraria;
using Terraria.GameContent.LootSimulation.LootSimulatorConditionSetterTypes;
using Terraria.ID;
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

    public static SpawnAnNPCRewriteData RewriteMethod(MethodInfo? methodOverride = null, bool allowUnknownPatterns = true, bool debug = false)
    {
        MethodInfo method = methodOverride ?? Utils.GetMethodOrThrow<NPC.Spawner>("SpawnAnNPC",
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(int),
            ]
        );

        DynamicMethodDefinition dmd = new(method);

        int size = 0;
        foreach (Instruction instr in dmd.Definition.Body.Instructions)
        {
            size += instr.GetSize();
        }

        float sizeKb = (float)size / 1024;

        Console.WriteLine($"{method.Name} simulator codegen begin, code size: {sizeKb:0.00}kb, instructions: {dmd.Definition.Body.Instructions.Count}, locals: {dmd.Definition.Body.Variables.Count}");

        SimulatorCodegen codegen = new(dmd, new StaticdataFieldNameParameterHandler(SpawnAnNpcStaticData.ProvideArg))
        {
            DebugPrintout = debug
        };

        HashSet<FieldInfo> allowFields = new();
        HashSet<MethodBase> allowMethods = [

            Utils.GetMethodOrThrow<NPC>("AnyNPCs"),
            Utils.GetMethodOrThrow<NPC>("CountNPCS"),
            Utils.GetMethodOrThrow<NPC>("AnyDanger"),

            Utils.GetMethodOrThrow<WorldGen>("SolidTile", [typeof(int), typeof(int), typeof(bool)]),
            Utils.GetMethodOrThrow<Collision>("SolidTiles", [typeof(int), typeof(int), typeof(int), typeof(int)]),

            Utils.GetMethodOrThrow(typeof(RuntimeHelpers), "InitializeArray", [typeof(Array), typeof(RuntimeFieldHandle)]),

            typeof(Rectangle).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(int)])!,
        ];

        ReplaceGetZombieSetings(codegen.IL);
        RemoveDefaultTargetSet(codegen.IL);
        PatchFindNearbyBook(codegen.IL, allowMethods);
        PatchGnomeInlineExpr(codegen.IL);

        InlineCalls(codegen.IL);

        PatchSlimes(codegen.IL);

        codegen.IL.Instrs.FillWithFakeILOffsets();
        codegen.AnalyzeStack();

        RewriteSpawnNPCCalls(codegen);
        RewriteSpawnOnPlayerCalls(codegen);

        codegen.PrepareForNodeCodegen();
        
        SimulationLuckMethods luck = new()
        {
            GetLuck = sim => ((SpawnAnNpcStaticData)sim.staticSimData).spawner.luck,
            OnLuckBasedBranchHit = (sim, direct) => ((SpawnAnNpcBlockResult)sim.GetCurrentBlockRunResult()).AddLuck(direct),
        };

        RandomCallNodeCodegen.RewriteRandomCalls(codegen, allowUnknownPatterns, luck);
        RewriteChanceFields(codegen);

        ILCursor c = new(codegen.IL);

        if (c.TryGotoNext(
            x => x.MatchLdsfld<Main>("rand")
        ))
        {
            throw new Exception($"Found Main.rand reference at IL_{c.Next!.Offset:x4}, should have none left");
        }

        codegen.AnalyzeStack();

        bool possiblyNonDeterministic = !VerifyNoSideEffects(c, allowFields, allowMethods, codegen.Stack, false);

        SimulatedMethod simmethod = codegen.GenerateMethod();

        return new(simmethod, codegen.Nodes.ToArray(), codegen.LocalStateType!, possiblyNonDeterministic);
    }

    static void RewriteSpawnNPCCalls(SimulatorCodegen codegen)
    {
        ILCursor c = new(codegen.IL);

        ulong unknownSpawns = 0;
        ulong knownSpawns = 0;

        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<NPC.Spawner>("SpawnNPC")
        ))
        {
            MethodReference spawnNpcMethod = (MethodReference)c.Next!.Operand;
            InstructionStackInfo stackInfo = codegen.Stack!.LookupInstruction(c.Next, out _)
                ?? throw new InvalidOperationException("Stack analysis out of date or invalid");

            int restParams = spawnNpcMethod.Parameters.Count;
            StackValue value = stackInfo.inValues[stackInfo.inValues.Count - 1 - restParams];

            if (value.producedBy.Count != 1)
                throw new InvalidOperationException($"Invalid this param value source for random call at IL_{c.Next.Offset:x4}");

            Instruction thisLoadInstr = value.producedBy[0];

            if (!thisLoadInstr.MatchLdarg(0))
                throw new InvalidOperationException($"Invalid this param value source (at IL_{thisLoadInstr.Offset:x4}) for random call at IL_{c.Next.Offset:x4}");

            thisLoadInstr.OpCode = OpCodes.Ldarg;
            thisLoadInstr.Operand = codegen.SimulatorParam;

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
                c.Goto(c.Index - 6, MoveType.AfterLabel);
                c.RemoveRange(7);
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
            }

            c.Emit<SpawnAnNpcHelpers>(OpCodes.Call, nameof(SpawnAnNpcHelpers.SpawnNPC));

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
            else if (c.Next.MatchBr(out ILLabel? brTarget) && brTarget.Target!.OpCode == OpCodes.Pop)
            {
                c.Emit(OpCodes.Ldnull); // todo: make CallInliner move Pops before branch
                continue;
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

    static void RewriteSpawnOnPlayerCalls(SimulatorCodegen codegen)
    {
        ILCursor c = new(codegen.IL);

        while (c.TryGotoNext(
            MoveType.AfterLabel,
            x => x.MatchCallOrCallvirt<NPC>("SpawnOnPlayer")
        ))
        {
            Instruction instr = c.Next!;
            InstructionStackInfo stackinfo = codegen.Stack!.LookupInstruction(instr, out _)!;

            StackValue targetValue = stackinfo.inValues[stackinfo.inValues.Count - 6];

            if (targetValue.producedBy.Count != 1)
            {
                // todo: warnings
                Console.WriteLine($"Unsupported target value producers for NPC.SpawnOnPlayer at IL_{instr.Offset:x4}");
                continue;
            }

            Instruction targetProducer = targetValue.producedBy[0];

            if (!targetProducer.MatchLdarg(6))
            {
                Console.WriteLine($"Unsupported target value producer for NPC.SpawnOnPlayer at IL_{instr.Offset:x4}");
                continue;
            }

            targetProducer.OpCode = OpCodes.Ldarg;
            targetProducer.Operand = codegen.SimulatorParam;

            if (SpawnAnalyzer.MatchInstructions(c.Context, c.Index - 4, out _,
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _),
                x => x.MatchLdcR4(out _)
            ))
            {
                c.Goto(c.Index - 4, MoveType.AfterLabel);
                c.RemoveRange(4);
            }
            else
            {
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
            }

            c.Remove();

            c.Emit<SpawnAnNpcHelpers>(OpCodes.Call, nameof(SpawnAnNpcHelpers.SpawnOnPlayer));
        }
    }

    static bool VerifyNoSideEffects(ILCursor c, IEnumerable<FieldInfo> allowFields, IEnumerable<MethodBase> allowMethods, StackAnalysis? stack, bool nested)
    {
        int sideEffects = 0;

        c.Index = 0;

        while (c.TryGotoNext(
            x => OutsideWritingOpcodes.Contains(x.OpCode)
        ))
        {
            if (c.Next!.Operand is MethodReference method)
            {
                MethodBase resolved = method.ResolveReflection();

                bool okMethod = false;
                foreach (var p in resolved.GetParameters())
                {
                    if (p.ParameterType == typeof(Simulator))
                    {
                        okMethod = true;
                        continue;
                    }
                }

                if (okMethod)
                {
                    continue;
                }

                Type? declaringType = resolved.DeclaringType;
                if (declaringType is not null)
                {
                    if (
                        declaringType == typeof(Math)
                     || declaringType.FullName == "System.MathF"
                     || declaringType == typeof(Simulator)
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

                DynamicMethodDefinition dmd;
                try
                {
                    dmd = new(resolved);
                }
                catch (Exception ex)
                {
                    throw new AggregateException($"Exception while loading method {resolved.Name} for side-effect analysis", ex);
                }
                ILContext ilc = new(dmd.Definition);

                if (VerifyNoSideEffects(new(ilc), allowFields, allowMethods, null, true))
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
            else if (stack is not null && StindOpcodes.Contains(c.Next.OpCode))
            {
                InstructionStackInfo? info = stack.LookupInstruction(c.Next, out _);
                if (info is not null)
                {
                    StackValue valref = info.inValues[info.inValues.Count - 2];
                    if (valref.producedBy.All(p => p.OpCode == OpCodes.Ldloca || p.OpCode == OpCodes.Ldloca_S))
                    {
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
            Utils.GetMethodOrThrow<NPC.Spawner>("CheckToSpawnSpider"),
            Utils.GetMethodOrThrow<NPC.Spawner>("CheckToSpawnRockGolem"),
            Utils.GetMethodOrThrow<NPC.Spawner>("CheckToSpawnUndergroundFairy"),

            Utils.GetMethodOrThrow<NPC.Spawner>("GetBasicSlimeToSpawn"),
            Utils.GetMethodOrThrow<NPC.Spawner>("GetGemSquirrelToSpawn"),
            Utils.GetMethodOrThrow<NPC.Spawner>("GetGemBunnyToSpawn"),

            Utils.GetMethodOrThrow<NPC.Spawner>("SpawnHornet"),
            Utils.GetMethodOrThrow<NPC.Spawner>("SpawnFrog"),
            Utils.GetMethodOrThrow<NPC.Spawner>("SpawnLavaBaitCritters"),

            Utils.GetMethodOrThrow<NPC.Spawner>("CheckToSpawnUndergroundGnomes"),

            Utils.GetMethodOrThrow<NPC>("FindCattailTop"),
            Utils.GetMethodOrThrow<NPC>("NearSpikeBall"),
        ];

        CallInliner.InlineAllCalls(il, m => inlineMethodsPass1.Any(m.Is) || IsATestInlineMethod(m));

        MethodBase[] inlineMethodsPass2 = [
            Utils.GetMethodOrThrow<NPC.Spawner>("GetBasicSlimeToSpawn_ChanceToBeHolidaySlime"),
        ];

        CallInliner.InlineAllCalls(il, m => inlineMethodsPass2.Any(m.Is));
    }

    static void ReplaceGetZombieSetings(ILContext il)
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
        ))
        {
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

    static void RemoveDefaultTargetSet(ILContext il)
    {
        /*
            IL_AAAB: ldarg.0
            IL_AAAC: ldarg.s   target (6)
            IL_AAAE: stfld     int32 Terraria.NPC/Spawner::defaultTarget
        */

        ILCursor c = new(il);

        while (c.TryGotoNext(
            MoveType.AfterLabel,
            x => x.MatchLdarg(0),
            x => x.MatchLdarg(6),
            x => x.MatchStfld<NPC.Spawner>("defaultTarget")
        ))
        {
            c.RemoveRange(3);
        }
    }

    static void PatchFindNearbyBook(ILContext il, HashSet<MethodBase> allowMethods)
    {
        MethodInfo helper;
        try
        {
            helper = RewriteFindNearbyBook();
        }
        catch (Exception e)
        {
            Console.WriteLine($"PatchFindNearbyBook: could not generate helper, {e}");
            return;
        }

        allowMethods.Add(helper);
        allowMethods.Add(Utils.GetMethodOrThrow<FindNearbyBookReturnValue>(nameof(FindNearbyBookReturnValue.GetPoint)));

        ILCursor c = new(il);

        /*
            |||/     ldloca.s  bookPosition
            ||||/    (push)
            |||||/   (push)
            >\\\\\  -call      bool Terraria.NPC::AI_FindNearbyBook(Point, int32, int32, Point&, bool, bool)

            >\\\\\  +call      FindNearbyBookReturnValue FindNearbyBookHelper(Point, int32, int32, Point&, bool, bool)
            |      
            |       // If not found, bail
            >\      +dup
            |>      +ldfld      FindNearbyBookReturnValue::found
            |\      +brfalse    endWithLoad
            |
            |       // If points are null, exit early
            >\      +dup
            |>      +ldfld      FindNearbyBookReturnValue::points
            |\      +brfalse    endWithLoad
            |
            |       // Choose a random point
            >\      +dup
            |>      +ldfld      FindNearbyBookReturnValue::points
            |>      +ldlen
            |>      +conv.i4
            ||
            ||      // Fake random call will be replaced
            |>      +call       int32 RandomCallNodeCodegen::FakeRandomNext(int32)
            |>      +call       int32 RandomCallNodeCodegen::AllUniqueValueHandlerMarker(int32)
            ||/     +ldloca.s   bookPosition
            \\\     +call       FindNearbyBookReturnValue::GetPoint(FindNearbyBookReturnValue, int32, Point&)
            >       +ldc.i4.1
            |       +br         end
    
            |   endWithLoad:
            >       +ldfld      FindNearbyBookReturnValue::found
            |   end:
        \*/

        while (c.TryGotoNext(
            MoveType.AfterLabel,
            x => x.MatchCall<NPC>("AI_FindNearbyBook")
        ))
        {
            if (!c.Previous.Previous.Previous.MatchLdloca(out int bookPosition))
            {
                Console.WriteLine($"PatchFindNearbyBook: weird bookPosition ref load for AI_FindNearbyBook at IL_{c.Next!.Offset:x4}");
                continue;
            }

            ILLabel endWithLoad = c.DefineLabel();
            ILLabel end = c.DefineLabel();

            c.Remove();
            c.Emit(OpCodes.Call, helper);

            c.Emit(OpCodes.Dup);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Ldfld, nameof(FindNearbyBookReturnValue.found));
            c.Emit(OpCodes.Brfalse, endWithLoad);

            c.Emit(OpCodes.Dup);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Ldfld, nameof(FindNearbyBookReturnValue.points));
            c.Emit(OpCodes.Brfalse, endWithLoad);

            c.Emit(OpCodes.Dup);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Ldfld, nameof(FindNearbyBookReturnValue.points));
            c.Emit(OpCodes.Ldlen);
            c.Emit(OpCodes.Conv_I4);

            c.Emit(OpCodes.Call, Utils.GetMethodOrThrow(typeof(RandomCallNodeCodegen), nameof(RandomCallNodeCodegen.FakeRandomNext)));
            c.Emit(OpCodes.Call, Utils.GetMethodOrThrow(typeof(RandomCallNodeCodegen), nameof(RandomCallNodeCodegen.AllUniqueValueHandlerMarker)));
            c.Emit(OpCodes.Ldloca, bookPosition);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Call, nameof(FindNearbyBookReturnValue.GetPoint));
            c.Emit(OpCodes.Ldc_I4_1);
            c.Emit(OpCodes.Br, end);

            c.MarkLabel(endWithLoad);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Ldfld, nameof(FindNearbyBookReturnValue.found));

            c.MarkLabel(end);
        }
    }

    static void PatchGnomeInlineExpr(ILContext il)
    {
        /*
             br.s      
             ldc.i4.0
            +stloc     validGnomeSpawn
            +ldloc     validGnomeSpawn

             ldloc.s   gnomeChance
             call      instance bool Terraria.NPC/Spawner::CheckToSpawnUndergroundGnomes(int32, int32, bool, int32)
        */

        ILCursor c = new(il);

        if (!c.TryGotoNext(
            x => x.MatchBr(out _),
            x => x.MatchLdcI4(0),
            x => x.MatchLdloc(out _),
            x => x.MatchCall<NPC.Spawner>("CheckToSpawnUndergroundGnomes")
        ))
        {
            return;
        }

        VariableDefinition validGnomeSpawn = new(il.Import(typeof(bool)));

        c.Goto(c.Index + 2, MoveType.AfterLabel);

        c.Emit(OpCodes.Stloc, validGnomeSpawn);
        c.Emit(OpCodes.Ldloc, validGnomeSpawn);

        il.Body.Variables.Add(validGnomeSpawn);
    }

    public class FindNearbyBookReturnValue
    {
        public bool found;
        public Point[]? points;

        public void GetPoint(int index, out Point val)
        {
            val = points![index];
        }

        public static FindNearbyBookReturnValue Create(bool retvalue, bool closestBook, Point[] points, int pointsLen)
        {
            FindNearbyBookReturnValue ret = new()
            {
                found = retvalue,
                points = null,
            };
            if (closestBook)
                return ret;

            Point[] newPoints = new Point[pointsLen];
            Array.Copy(points, newPoints, pointsLen);

            ret.points = newPoints;
            return ret;
        }
    }

    public delegate FindNearbyBookReturnValue FindNearbyBookHelperDelegate(
        Point searchPosition, int searchWidth, int searchHeight,
        out Point bookPosition, bool closestBook, bool checkPlayerScreenRanges
    );

    static MethodInfo RewriteFindNearbyBook()
    {
        DynamicMethodDefinition dmd = new(Utils.GetMethodOrThrow<NPC>("AI_FindNearbyBook"));

        dmd.Definition.ReturnType = dmd.Module.ImportReference(typeof(FindNearbyBookReturnValue));

        ParameterDefinition closestBook = dmd.Definition.Parameters[4];

        ILContext il = new(dmd.Definition);

        il.Invoke(il =>
        {
            ILCursor c = new(il);

            int nearbyBooks = 0;
            int nearbyBooksLength = 0;

            /*
                -ldarg.3
	            -ldloc     nearbyBooks
	            -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
	            -ldloc     nearbyBooksLength
	            -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
	            -ldelem    [FNA]Microsoft.Xna.Framework.Point
	            -stobj     [FNA]Microsoft.Xna.Framework.Point
            */

            if (!c.TryGotoNext(
                MoveType.AfterLabel,
                x => x.MatchLdarg(3),
                x => x.MatchLdloc(out nearbyBooks),
                x => x.MatchLdsfld<Main>("rand"),
                x => x.MatchLdloc(out nearbyBooksLength),
                x => x.MatchCallvirt<UnifiedRandom>("Next"),
                x => x.MatchLdelemAny(out _),
                x => x.MatchStobj(out _)
            ))
            {
                throw new Exception("RewriteFindNearbyBook: failed to match random call");
            }

            c.RemoveRange(7);

            ILLabel retHandler = c.DefineLabel();
            foreach (Instruction instr in c.Instrs)
            {
                if (instr.OpCode == OpCodes.Ret)
                {
                    instr.OpCode = OpCodes.Br;
                    instr.Operand = retHandler;
                }
            }

            Instruction ret = c.IL.Create(OpCodes.Ret);
            c.Instrs.Add(ret);

            c.Goto(ret);

            /*
                retHandler:
                    ldarg      closestBook
                    ldloc      nearbyBooks
                    ldloc      nearbyBooksLength
                    call       FindNearbyBookReturnValue FindNearbyBookReturnValue::Create(bool, bool, Point[], int32)
            */

            c.MarkLabel(retHandler);
            c.Emit(OpCodes.Ldarg, closestBook);
            c.Emit(OpCodes.Ldloc, nearbyBooks);
            c.Emit(OpCodes.Ldloc, nearbyBooksLength);
            c.Emit<FindNearbyBookReturnValue>(OpCodes.Call, nameof(FindNearbyBookReturnValue.Create));
        });

        DMDHack.SetNullOriginalMethod(dmd);

        return dmd.Generate().CreateDelegate<FindNearbyBookHelperDelegate>().Method;
    }

    class ILLabelTemplate
    {
        public int targetIndex;

        public ILLabelTemplate(int targetIndex)
        {
            this.targetIndex = targetIndex;
        }
    }

    static void PatchSlimes(ILContext il)
    {
        DynamicMethodDefinition spawnNPC = new(Utils.GetMethodOrThrow<NPC.Spawner>("SpawnNPC", [
            /*                [0] this   */
            typeof(int),   /* [1] X      */ 
            typeof(int),   /* [2] Y      */ 
            typeof(int),   /* [3] Type   */ 
            typeof(int),   /* [4] Start  */ 
            typeof(float), /* [5] ai0    */   
            typeof(float), /* [6] ai1    */   
            typeof(float), /* [7] ai2    */   
            typeof(float), /* [8] ai3    */   
            typeof(int),   /* [9] Target */ 
        ]));

        ILContext sil = new(spawnNPC.Definition);
        ILCursor sc = new(sil);

        Instruction slimeCodeEndInstruction = null!;

        /*
        [0] ldarg.3
	    [1] call      int32 Terraria.ID.NPCID::FromNetId(int32)
	    [2] ldc.i4.1
	    [3] bne.un.s  slimeCodeEnd
        */

        if (!sc.TryGotoNext(
            x => x.MatchLdarg(3),
            x => x.MatchCall(typeof(NPCID), "FromNetId"),
            x => x.MatchLdcI4(1),
            x =>
            {
                bool b = x.OpCode == OpCodes.Bne_Un || x.OpCode == OpCodes.Bne_Un_S;
                if (b) slimeCodeEndInstruction = (Instruction)x.Operand;
                return b;
            }
        ))
        {
            Console.WriteLine("PatchSlimes: failed to match spawn code beginning");
            return;
        }

        int fastSlimeCodeOffset = 4;

        Dictionary<Instruction, ILLabelTemplate> slimeCodeLabelTemplates = new();
        slimeCodeLabelTemplates.Add(slimeCodeEndInstruction, new(-1));

        List<(OpCode, object)> slimeCodeTemplate = new();

        VariableDefinition npcType = new(il.Import(typeof(int)));

        int startIndex = sc.Index;

        for (int i = 0; ; i++)
        {
            if ((startIndex + i) >= sc.Instrs.Count)
            {
                Console.WriteLine("PatchSlimes: ran off the end while trying to fing the end of slime code");
                return;
            }
            Instruction instr = sc.Instrs[startIndex + i];

            if (instr == slimeCodeEndInstruction)
                break;

            if (instr.Operand is Instruction target)
            {
                if (!slimeCodeLabelTemplates.TryGetValue(target, out ILLabelTemplate? targetLabel))
                {
                    targetLabel = new(-1);
                    slimeCodeLabelTemplates.Add(target, targetLabel);
                }
                instr.Operand = targetLabel;
            }
            if (instr.MatchLdarg(0))
            {
                instr.Operand = il.Method.Parameters[0];
            }
            if (instr.MatchLdarg(3))
            {
                instr.OpCode = OpCodes.Ldloc;
                instr.Operand = npcType;
            }
            else if (instr.MatchStarg(3))
            {
                instr.OpCode = OpCodes.Stloc;
                instr.Operand = npcType;
            }
            else if (instr.MatchRet())
            {
                instr.OpCode = OpCodes.Br;
                instr.Operand = new ILLabelTemplate(-1);
            }

            slimeCodeTemplate.Add((instr.OpCode, instr.Operand));
        }

        for (int i = 0; i < slimeCodeTemplate.Count; i++)
        {
            if (slimeCodeLabelTemplates.TryGetValue(sc.Instrs[startIndex + i], out ILLabelTemplate? template))
            {
                template.targetIndex = i;
            }
        }

        foreach (var (instr, label) in slimeCodeLabelTemplates)
        {
            if (instr == slimeCodeEndInstruction)
            {
                continue;
            }
            if (label.targetIndex < 0)
            {
                Console.WriteLine("PatchSlimes: slime code jumps into unexpected places");
                return;
            }
        }

        StackAnalysis stack = StackAnalyzer.Analyze(il);

        /*
            Before each SpawnNPC, check if statically known spawn npc type matches,
            set returnId, jump to slimeHandler, passing npc type on the stack

            slimeHandler runs the slime code from the beginning on SpawnNPC, then jumps back to returnLabels[returnId]
        */

        // variables will be preserved across random calls
        il.Body.Variables.Add(npcType);

        ILCursor c = new(il);

        Dictionary<ILLabelTemplate, ILLabel> slimeCodeLabelInstances = new();

        ulong patched = 0;
        ulong skipped = 0;
        ulong patchedEarly = 0;

        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<NPC.Spawner>("SpawnNPC")
        ))
        {
            Instruction spawnNPCinstr = c.Next!;
            InstructionStackInfo stackInfo = stack.LookupInstruction(spawnNPCinstr, out _)!;

            StackValue typeValue = stackInfo.inValues[stackInfo.inValues.Count - 7];

            Instruction? earliestInstruction = null;

            if (typeValue.producedBy.Count == 1)
            {
                StackValue thisValue = stackInfo.inValues[stackInfo.inValues.Count - 10];

                if (thisValue.producedBy.Count == 1 && thisValue.producedBy[0].MatchLdarg(0))
                {
                    earliestInstruction = thisValue.producedBy[0];
                }
            }

            foreach (Instruction producer in typeValue.producedBy)
            {
                bool doSlimeCode = CanThisInstructionOutputASlimeType(il, producer, stack, out bool doFastSlimeCode, 5) is true or null;

                if (!doSlimeCode)
                {
                    skipped++;
                    continue;
                }

                bool early = false;
                if (earliestInstruction is not null && producer.MatchLdcI4(out _) || producer.MatchLdloc(out _))
                {
                    early = true;
                    c.Goto(producer, MoveType.AfterLabel);
                    c.Remove();

                    c.Emit(OpCodes.Ldloc, npcType);
                    c.Emit(OpCodes.Ldc_I4_0);
                    c.Emit(OpCodes.Stloc, npcType);

                    c.Goto(earliestInstruction, MoveType.AfterLabel);
                    c.Emit(producer.OpCode, producer.Operand);
                }
                else
                {
                    c.Goto(producer, MoveType.After);
                }

                c.Emit(OpCodes.Stloc, npcType);

                startIndex = c.Index;
                int slimeCodeStartIndex = 0;
                if (doFastSlimeCode)
                    slimeCodeStartIndex = fastSlimeCodeOffset;


                slimeCodeLabelInstances.Clear();
                foreach (var label in slimeCodeLabelTemplates.Values)
                {
                    slimeCodeLabelInstances.Add(label, il.DefineLabel());
                }

                for (int i = slimeCodeStartIndex; i < slimeCodeTemplate.Count; i++)
                {
                    var (opCode, operand) = slimeCodeTemplate[i];

                    if (operand is ILLabelTemplate template)
                    {
                        operand = slimeCodeLabelInstances[template];
                    }

                    c.Emit(opCode, operand);
                }

                foreach (var (template, instance) in slimeCodeLabelInstances)
                {
                    if (template.targetIndex < 0)
                    {
                        c.MarkLabel(instance);
                    }
                    else
                    {
                        instance.Target = c.Instrs[startIndex + template.targetIndex - slimeCodeStartIndex];
                    }
                }

                patched++;

                if (!early)
                {
                    c.Emit(OpCodes.Ldloc, npcType);
                    c.Emit(OpCodes.Ldc_I4_0);
                    c.Emit(OpCodes.Stloc, npcType);
                }
                else
                {
                    patchedEarly++;
                }
            }
            c.Goto(spawnNPCinstr, MoveType.After);
        }

        Console.WriteLine($"PatchSlimes finished, {patched} places patched, {skipped} skipped, {patchedEarly} places patched in early mode");
    }

    static bool? CanThisInstructionOutputASlimeType(ILContext il, Instruction instr, StackAnalysis stack, out bool allValuesAreSlimeTypes, int recursionLimit)
    {
        allValuesAreSlimeTypes = false;

        if (recursionLimit <= 0)
            return null;

        if (instr.MatchLdcI4(out int staticType))
        {
            if (NPCID.FromNetId(staticType) == 1)
            {
                allValuesAreSlimeTypes = true;
                return true;
            }
            else
            {
                return false;
            }
        }
        else if (instr.MatchLdloc(out int loc))
        {
            foreach (Instruction instr2 in il.Instrs)
            {
                if (instr2.MatchLdloca(loc))
                {
                    return null;
                }
                if (!instr2.MatchStloc(loc))
                    continue;

                InstructionStackInfo? svi = stack.LookupInstruction(instr2, out _);
                if (svi is null)
                    return null;

                StackValue sv = svi.inValues[svi.inValues.Count - 1];

                foreach (Instruction valueProducer in sv.producedBy)
                {
                    var res = CanThisInstructionOutputASlimeType(il, valueProducer, stack, out _, recursionLimit - 1);

                    if (res is not false)
                        return res;
                }
            }
            return false;
        }
        else if (instr.MatchCallOrCallvirt<UnifiedRandom>("Next"))
        {
            MethodReference next = (MethodReference)instr.Operand;

            if (next.Parameters.Count != 2)
                return null;

            InstructionStackInfo? stackInfo = stack.LookupInstruction(instr, out _);
            if (stackInfo is null)
                return null;

            int rangeStart = 0;
            int rangeEndExcl = 0;

            StackValue min = stackInfo.inValues[stackInfo.inValues.Count - 2];
            StackValue max = stackInfo.inValues[stackInfo.inValues.Count - 1];

            if (min.producedBy.Count != 1 || !min.producedBy[0].MatchLdcI4(out rangeStart))
            {
                return null;
            }

            if (max.producedBy.Count != 1 || !min.producedBy[0].MatchLdcI4(out rangeEndExcl))
            {
                return null;
            }

            for (int i = rangeStart; i < rangeEndExcl; i++)
            {
                if (NPCID.FromNetId(i) == 1)
                {
                    return true;
                }
            }

            return false;
        }
        else if (instr.MatchCall("Terraria.Utils", "SelectRandom"))
        {
            int pos = il.Instrs.IndexOf(instr);

            /*
                ldc.i4.s  11
		        newarr    [System.Runtime]System.Int32
		        dup
		        ldtoken   field valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=44' '<PrivateImplementationDetails>'::F3BD6507212EB75B6DB7F1C7806475496C0F44846A87675EED189766EC7609D1
		        call      void [System.Runtime]System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(class [System.Runtime]System.Array, valuetype [System.Runtime]System.RuntimeFieldHandle)
            */

            int matchPos = pos - 4;

            int matchedIncrementalInits = 0;
            int val = 0;

            while (matchPos >= 0 && SpawnAnalyzer.MatchInstructions(
                il, matchPos, out _,
                x => x.MatchDup(),
                x => x.MatchLdcI4(out _),
                x => x.MatchLdcI4(out val),
                x => x.MatchStelemI4()
            ))
            {
                matchPos -= 4;
                matchedIncrementalInits++;
                if (NPCID.FromNetId(val) == 1)
                {
                    return true;
                }
            }

            if (matchedIncrementalInits > 0 && matchPos >= 0)
            {
                matchPos += 2;
                if (!SpawnAnalyzer.MatchInstructions(
                    il, matchPos, out _,
                    x => x.MatchLdcI4(out _),
                    x => x.MatchNewarr(out _)
                ))
                {
                    return null;
                }

                return false;
            }

            matchPos = pos - 5;
            if (matchPos < 0)
                return null;

            int arrayLen = 0;
            TypeReference valueType = null!;
            IMetadataTokenProvider fieldProvider = null!;

            if (!SpawnAnalyzer.MatchInstructions(
                il, matchPos, out _,
                x => x.MatchLdcI4(out arrayLen),
                x => x.MatchNewarr(out valueType!),
                x => x.MatchDup(),
                x => x.MatchLdtoken(out fieldProvider!),
                x => x.MatchCall(out MethodReference? m) && m!.Name == "InitializeArray"
            ))
            {
                return null;
            }

            if (fieldProvider is not FieldReference field)
                return null;

            RuntimeFieldHandle handle = field.ResolveReflection().FieldHandle;
            IEnumerable<int> values;

            if (valueType.Is(typeof(int)))
            {
                int[] data = new int[arrayLen];
                RuntimeHelpers.InitializeArray(data, handle);
                values = data;
            }
            else if (valueType.Is(typeof(short)))
            {
                short[] data = new short[arrayLen];
                RuntimeHelpers.InitializeArray(data, handle);
                values = data.Select(d => (int)d);
            }
            else
            {
                return null;
            }

            foreach (int v in values)
            {
                if (NPCID.FromNetId(v) == 1)
                {
                    return true;
                }
            }
            return false;
        }
        else if (instr.MatchCall<SpawnAnNPCRewriter>(nameof(DontPatchSlimesMarker)))
        {
            return false;
        }

        // Terraria.Utils.SelectRandom also gets used in 9 places, can be checked too

        return null;
    }

    static void RewriteChanceFields(SimulatorCodegen codegen)
    {
        ILCursor c = new(codegen.IL);

        List<FieldInfo> spawnerChanceFields = typeof(SpawnerChances)
            .GetFields()
            .Where(f => !f.IsStatic && f.Name.EndsWith("Chance"))
            .Select(f => typeof(NPC.Spawner).GetField(f.Name.Substring(0, f.Name.Length - 6), (BindingFlags)(-1)))
            .Where(f => f is not null)
            .ToList()!;

        FieldReference? field = null;

        MethodInfo getCurrentTimelineState = Utils.GetMethodOrThrow<Simulator>(nameof(Simulator.GetCurrentTimelineState));

        while (c.TryGotoNext(
            x => x.MatchLdfld(out field) && spawnerChanceFields.Any(field.Is)
        ))
        {
            Instruction ldfld = c.Next!;
            bool needsPop = true;
            if (c.Prev?.MatchLdarg(0) ?? false)
            {
                c.Goto(c.Prev);
                if (!c.IncomingLabels.Any())
                {
                    c.Remove();
                    needsPop = false;
                }
            }
            c.Goto(ldfld, MoveType.AfterLabel);
            if (needsPop)
            {
                c.Emit(OpCodes.Pop);
            }

            c.Remove();

            List<StackValue> outValues = codegen.Stack!.LookupInstruction(ldfld, out _)!.outValues;

            StackValue[] stackValues = new StackValue[outValues.Count - 1];
            for (int i = 0; i < stackValues.Length; i++)
            {
                stackValues[i] = outValues[i];
            }

            ILLabel stateNonNull = c.DefineLabel();
            ILLabel node = c.DefineLabel();
            ILLabel result = c.DefineLabel();

            VariableDefinition tempNullBoolVar = codegen.TempLocals.AllocVariable(c.IL.Import(typeof(bool?)));

            c.Emit(OpCodes.Ldarg, codegen.SimulatorParam);
            c.Emit(OpCodes.Call, getCurrentTimelineState);

            c.Emit(OpCodes.Ldfld, Utils.GetFieldOrThrow<SpawnAnNpcTimelineState>(field!.Name));
            c.Emit(OpCodes.Stloc, tempNullBoolVar);
            c.Emit(OpCodes.Ldloca, tempNullBoolVar);
            c.Emit(OpCodes.Call, Utils.GetMethodOrThrow<bool?>("get_HasValue"));
            c.Emit(OpCodes.Brfalse, node);

            c.Emit(OpCodes.Ldloca, tempNullBoolVar);
            c.Emit(OpCodes.Call, Utils.GetMethodOrThrow<bool?>("get_Value"));
            c.Emit(OpCodes.Br, result);

            c.MarkLabel(node);

            codegen.EmitNode(c, FieldChanceNode.GetForField(field!.Name), NodeParameterInputBehavior.AlwaysNull, stackValues);

            c.MarkLabel(result);

            codegen.TempLocals.FreeVariable(tempNullBoolVar);
        }
    }

    public static T DontPatchSlimesMarker<T>(T val) {
        return val;
    }
}

public class SpawnAnNpcHelpers
{
    public static void SpawnNPC(Simulator sim, int x, int y, int type)
    {
        ((SpawnAnNpcBlockResult)sim.GetCurrentBlockRunResult()).ExitNodeHit_SpawnNPC(x, y, type);
    }

    public static void SpawnOnPlayer(Simulator sim, int type)
    {
        ((SpawnAnNpcBlockResult)sim.GetCurrentBlockRunResult()).ExitNodeHit_SpawnOnPlayer(type);
    }
}

public class SpawnAnNPCRewriteData
{
    public SimulatedMethod Method;

    public SimulationNodeInfo[] Nodes;

    public DynamicStateType LocalStateType;

    public bool PossiblyNonDeterministic;

    public SpawnAnNPCRewriteData(SimulatedMethod method, SimulationNodeInfo[] nodes, DynamicStateType localStateType, bool possiblyNonDeterministic)
    {
        Method = method;
        Nodes = nodes;
        LocalStateType = localStateType;
        PossiblyNonDeterministic = possiblyNonDeterministic;
    }
}

#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public class SpawnAnNpcTimelineState
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    public bool? spawnFriendly = null;
    public bool? noWorms = null;
    public bool? skyMob = null;
    public bool? nearMarble = null;
    public bool? nearGranite = null;
    public bool? spawnSpider = null;
    public bool? spawnUndergroundDesert = null;
    public bool? isBeach = null;
    public bool? isOcean = null;
    public bool? surfaceSpawn = null;
    public bool? dayTime = null;

    readonly static Func<object, object> CloneImpl = DynamicStateType.GenerateCloneMethod(typeof(SpawnAnNpcTimelineState));
    readonly static Func<object, object, bool> EqImpl = DynamicStateType.GenerateEqMethod(typeof(SpawnAnNpcTimelineState));

    public SpawnAnNpcTimelineState()
    {
    }

    public override bool Equals(object? obj)
    {
        if (obj is not SpawnAnNpcTimelineState other)
            return false;

        return EqImpl(this, other);
    }

    public SpawnAnNpcTimelineState Clone()
    {
        return (SpawnAnNpcTimelineState)CloneImpl(this);
    }

    public static DynamicStateType StateType = new(typeof(SpawnAnNpcTimelineState), () => new SpawnAnNpcTimelineState(), EqImpl, CloneImpl);
}

public class SpawnAnNpcStaticData
{
    public required NPC.Spawner spawner;
    public required int spawnTileX;
    public required int spawnTileY;
    public required int tileType;
    public required int wallType;
    public required bool xRange;
    public required int target;

    public required SpawnerChances chances;

    public static FieldInfo ProvideArg(int arg, string argname)
    {
        string fieldname = argname;
        if (arg == 0)
        {
            fieldname = nameof(spawner);
        }

        return Utils.GetFieldOrThrow<SpawnAnNpcStaticData>(fieldname);
    }
}

public enum BranchLuckType {
    None,
    Direct,
    Indirect,
    Mixed
}

public class SpawnAnNpcBlockResult
{
    public BranchLuckType BranchLuckType = BranchLuckType.None;
    public List<NextSpawn>? Spawns = null;

    public bool? GetLuckAffection() {
        return BranchLuckType switch
        {
            BranchLuckType.None => null,
            BranchLuckType.Direct => true,
            BranchLuckType.Indirect => false,
            BranchLuckType.Mixed => false,
            _ => throw new InvalidOperationException(),
        };
    }

    internal void AddLuck(bool direct) {
        switch (BranchLuckType) {
            case BranchLuckType.None:
                if (direct)
                    BranchLuckType = BranchLuckType.Direct;
                else 
                    BranchLuckType = BranchLuckType.Indirect;
                break;
            
            case BranchLuckType.Direct:
                if (!direct)
                    BranchLuckType = BranchLuckType.Mixed;
                break;

            case BranchLuckType.Indirect:
                if (direct)
                    BranchLuckType = BranchLuckType.Mixed;
                break;
        }
    }

    internal void ExitNodeHit_SpawnNPC(int x, int y, int type)
    {
        AddCurrentConnectionSpawn(new(type)
        {
            pos = new(x, y),
        });
    }

    internal void ExitNodeHit_SpawnOnPlayer(int type)
    {
        AddCurrentConnectionSpawn(new(type)
        {
            spawnOnPlayer = true,
        });
    }

    internal void AddCurrentConnectionSpawn(NextSpawn spawn)
    {
        Spawns ??= new();
        Spawns.Add(spawn);
    }
}

public struct NextSpawn
{
    public int npcId;

    public NextSpawn(int npcId) : this()
    {
        this.npcId = npcId;
    }

    public Point? pos = null;

    /// <summary>
    /// Whether the spawn was hit from simulation itself or from an uncontrolled helper method
    /// </summary>
    public bool leakedSpawn = false;

    /// <summary>
    /// Whether the spawn uses NPC.SpawnOnPlayer
    /// </summary>
    public bool spawnOnPlayer = false;
}

class FieldChanceNode : SimulationNodeImpl
{
    readonly Func<Simulator, float> getChance;
    readonly Action<Simulator> setFalse;
    readonly Action<Simulator> setTrue;

    static Dictionary<string, FieldChanceNode> cache = new();

    public static FieldChanceNode GetForField(string fieldName)
    {
        if (cache.TryGetValue(fieldName, out FieldChanceNode? node))
            return node;

        FieldInfo chanceField = Utils.GetFieldOrThrow<SpawnerChances>(fieldName + "Chance");
        FieldInfo stateField = Utils.GetFieldOrThrow<SpawnAnNpcTimelineState>(fieldName);
        FieldInfo staticSimDataField = Utils.GetFieldOrThrow<Simulator>(nameof(Simulator.staticSimData));
        FieldInfo chancesField = Utils.GetFieldOrThrow<SpawnAnNpcStaticData>(nameof(SpawnAnNpcStaticData.chances));

        MethodInfo getCurrentTimelineState = Utils.GetMethodOrThrow<Simulator>(nameof(Simulator.GetCurrentTimelineState));
        ConstructorInfo nullBoolCtor = typeof(bool?).GetConstructor([typeof(bool)]) ?? throw new MissingMethodException("bool? ctor");


        DynamicMethodDefinition dmd = new($"GetSpawnerChanceField_{fieldName}", typeof(float), [typeof(Simulator)]);

        ILProcessor il = dmd.GetILProcessor();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, staticSimDataField);
        il.Emit(OpCodes.Ldfld, chancesField);
        il.Emit(OpCodes.Ldfld, chanceField);
        il.Emit(OpCodes.Ret);

        Func<Simulator, float> getChance = dmd.Generate().CreateDelegate<Func<Simulator, float>>();


        dmd = new($"SetSimulationTimelineStateFieldFalse_{fieldName}", typeof(void), [typeof(Simulator)]);
        il = dmd.GetILProcessor();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getCurrentTimelineState);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, nullBoolCtor);
        il.Emit(OpCodes.Stfld, stateField);
        il.Emit(OpCodes.Ret);

        Action<Simulator> setFalse = dmd.Generate().CreateDelegate<Action<Simulator>>();

        dmd = new($"SetSimulationTimelineStateFieldTrue_{fieldName}", typeof(void), [typeof(Simulator)]);
        il = dmd.GetILProcessor();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getCurrentTimelineState);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, nullBoolCtor);
        il.Emit(OpCodes.Stfld, stateField);
        il.Emit(OpCodes.Ret);

        Action<Simulator> setTrue = dmd.Generate().CreateDelegate<Action<Simulator>>();

        node = new(getChance, setFalse, setTrue);

        cache.Add(fieldName, node);

        return node;
    }

    private FieldChanceNode(Func<Simulator, float> getChance, Action<Simulator> setFalse, Action<Simulator> setTrue)
    {
        this.getChance = getChance;
        this.setFalse = setFalse;
        this.setTrue = setTrue;
    }

    public override void NodeHit(Simulator sim, object param, out BranchInfo[] branches)
    {
        float chance = getChance(sim);

        if (chance >= 1f)
        {
            branches = [
                new() {
                    chance = 1f,
                    returnValue = 1,
                    onBranchSelected = setTrue,
                }
            ];
        }
        else if (chance <= 0f)
        {
            branches = [
                new() {
                    chance = 1f,
                    returnValue = 0,
                    onBranchSelected = setFalse,
                }
            ];
        }
        else
        {
            branches = [
                new() {
                    chance = chance,
                    returnValue = 1,
                    onBranchSelected = setTrue,
                },
                new() {
                    chance = 1f - chance,
                    returnValue = 0,
                    onBranchSelected = setFalse,
                }
            ];
        }
    }
}