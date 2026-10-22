using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using SpawnAnalyzer.Rewriters.SpawnAnNPC;
using SpawnAnalyzer.Simulation;
using SpawnAnalyzer.UI;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics;
using Terraria.ID;
using Terraria.UI;
using Terraria.Utilities;

namespace SpawnAnalyzer;

// TODO: Optimize 100% and 0% chances, merge same return value branches

// TODO: no side effects in the simulated function
// TODO: Spawner.ShouldSpawnInvasionEnemies in SetSpawnFlags

// TODO: nodes for NPCCount
// TODO: better selftests?
// TODO: anything that can go wrong, will go wrong, show warnings and errors

// TODO: build method block tree to determine when locals end
// TODO: Rules for chances depending on other chances, end of SetSpawnFlagsForChosenTile

// TODO: hovering over a spawn should highlight its spawning and final tiles
// TODO: textures for overlay instead of rectangles

// TODO: analyzer should be aware of infinite loops

// TODO: track extra spawn params (ai params)
// TODO: generic rewriter/simulator, simulate slime item, simulate spawner setup methods for valid combinations of states 

// TODO: pick luck in the ui, chance at lowest luck, chance at highest luck

public class SpawnAnalyzer
{
    public static SpawnAnalysis? LastAnalysis { get; private set; }

    public static SimulatorImpl? DefaultImpl;
    private static Thread? ImplInitThread;

    internal static Hook? MainUpdateHook;
    internal static Hook? MainUpdateUIStatesHook;
    internal static Hook? MainDrawMouseOverHook;
    internal static Hook? MainSetupDrawInterfaceLayersHook;
    internal static Hook? NPCSpawnerSpawnNPCHook;
    internal static Hook? NPCNewNPCHook;

    internal static ILHook? UnifiedRandomInternalSampleHook;

    internal static Hook[]? MonoModDynamicMethodsNREHooks;

    static Dictionary<string, Texture2D> TextureCache = new();

    static int DebugCountdown = -1;

    public static void InstallVanilla()
    {
        MainUpdateHook = new Hook(Utils.GetMethodOrThrow<Main>("Update"), On_Main_Update);
        MainUpdateUIStatesHook = new Hook(Utils.GetMethodOrThrow<Main>("UpdateUIStates"), On_Main_UpdateUIStates);
        MainDrawMouseOverHook = new Hook(Utils.GetMethodOrThrow<Main>("DrawMouseOver"), On_Main_DrawMouseOver);
        MainSetupDrawInterfaceLayersHook = new Hook(Utils.GetMethodOrThrow<Main>("SetupDrawInterfaceLayers"), On_Main_SetupDrawInterfaceLayers);

        NPCSpawnerSpawnNPCHook = new Hook(Utils.GetMethodOrThrow<NPC.Spawner>("SpawnNPC", [
            typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(float), typeof(float), typeof(float), typeof(float),
            typeof(int)
        ]), On_NPC_Spawner_SpawnNPC);

        NPCNewNPCHook = new Hook(Utils.GetMethodOrThrow<NPC>("NewNPC"), On_NPC_NewNPC);
        UnifiedRandomInternalSampleHook = new ILHook(Utils.GetMethodOrThrow<UnifiedRandom>("InternalSample"), IL_UnifiedRandom_InternalSample);

        HackMonoModDynamicMethodNRE();

        SelfTest(null, false, false, false);
        Environment.Exit(1);

        ImplInitThread = new Thread(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            DefaultImpl = SimulatorImpl.GenerateImpl();
            Console.WriteLine($"\x1b[1m\x1b[32mDone\x1b[39m SimulatorImpl.GenerateImpl in {sw.Elapsed.TotalMilliseconds:0.00}ms\x1b[0m");
        });
        ImplInitThread.Name = "SpawnAnalyzer DefaultImpl generator";
        ImplInitThread.Start();

        if (Program.LaunchParameters.ContainsKey("-spawnanalyzerdebug"))
            DebugCountdown = 10;
    }

    static void HackMonoModDynamicMethodNRE()
    {
        var dynamicMethod = new System.Reflection.Emit.DynamicMethod("method", typeof(void), [], true);

        var dmd = new DynamicMethodDefinition("", null, []);
        var il = dmd.GetILProcessor();

        var re = il.Import(dynamicMethod);

        List<Hook> hooks = new();

        try
        {
            re.Is("a", "a");
        }
        catch (NullReferenceException)
        {
            hooks.Add(
                new Hook(
                    Utils.GetMethodOrThrow(typeof(MonoMod.Utils.Extensions), "Is", [typeof(MethodReference), typeof(string), typeof(string)]),
                    (Func<MethodReference, string, string, bool> orig, MethodReference method, string typeFullName, string name) =>
                    {
                        if (method is DynamicMethodReference)
                            return false;

                        return orig(method, typeFullName, name);
                    }
                )
            );
        }

        try
        {
            re.Is(typeof(int), "a");
        }
        catch (NullReferenceException)
        {
            hooks.Add(
                new Hook(
                    Utils.GetMethodOrThrow(typeof(MonoMod.Utils.Extensions), "Is", [typeof(MethodReference), typeof(Type), typeof(string)]),
                    (Func<MethodReference, Type, string, bool> orig, MethodReference method, Type type, string name) =>
                    {
                        if (method is DynamicMethodReference)
                            return false;

                        return orig(method, type, name);
                    }
                )
            );
        }

        if (hooks.Count == 0)
            return;

        MonoModDynamicMethodsNREHooks = hooks.ToArray();
    }

    public static SimulatorImpl? GetDefaultImplBlocking()
    {
        if (DefaultImpl is not null)
            return DefaultImpl;

        if (ImplInitThread is null)
            return null;

        ImplInitThread.Join();
        return DefaultImpl;
    }

    public static Texture2D GetTexture(string path)
    {
        if (TextureCache.TryGetValue(path, out Texture2D? texture))
            return texture;

        Stream stream = typeof(SpawnAnalyzer).Assembly.GetManifestResourceStream($"SpawnAnalyzer.Assets.{path.Replace('/', '.')}.png")
            ?? throw new FileNotFoundException($"Could not find SpawnAnalyzer texture asset: {path}");

        texture = Texture2D.FromStream(Main.graphics.GraphicsDevice, stream);
        TextureCache.Add(path, texture);

        return texture;
    }

    public static bool SelfTest(int? specificTest = null, bool printNodes = false, bool ilprintout = false, bool printChances = false)
    {
        static bool MatchBlock(
            int testid,
            int testindex,
            int resultindex,
            TestMethods.TestBlock[] testBlocks,
            SimulationBlock[] blocks,
            bool report,
            int depth,
            ref int faildepth
        )
        {
            TestMethods.TestBlock testBlock = testBlocks[testindex];
            SimulationBlock block = blocks[resultindex];

            var result = block.results as SpawnAnNpcBlockResult;

            int gotSpawns = result?.Spawns?.Count ?? 0;
            if (gotSpawns != testBlock.spawns.Length)
            {

                faildepth = depth;
                if (report)
                    Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} result {resultindex}) Expected {gotSpawns} spawns, got {testBlock.spawns.Length}");
                return false;
            }

            for (int k = 0; k < gotSpawns; k++)
            {
                if (result!.Spawns![k].npcId != testBlock.spawns[k])
                {
                    faildepth = depth;
                    if (report)
                        Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} result {resultindex}) Expected spawn {k} to be {testBlock.spawns[k]}, got {result.Spawns[k].npcId}");
                    return false;
                }
            }

            var remainingBranches = block.nextBranches.ToList();

            if (block.nextBranches.Length != testBlock.branches.Length)
            {
                faildepth = depth;
                if (report)
                    Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} result {resultindex}) Expected {testBlock.branches.Length} branches, got {block.nextBranches.Length}");
                return false;
            }

            for (int i = 0; i < testBlock.branches.Length; i++)
            {
                var (branchChance, brahchNextIndex) = testBlock.branches[i];

                int? foundMatchingBranch = null;

                int maxFailDepth = 0;
                int? maxFailIndex = null;

                for (int j = 0; j < remainingBranches.Count; j++)
                {
                    var nextBranch = remainingBranches[j];

                    if (branchChance - 0.005f > nextBranch.chance || branchChance + 0.005f < nextBranch.chance)
                        continue;

                    if (!brahchNextIndex.HasValue)
                    {
                        var nextBlock = blocks[nextBranch.nextBlock];
                        var nextBlockResult = nextBlock.results as SpawnAnNpcBlockResult;

                        if (nextBlock.nextBranches.Length != 0)
                            continue;

                        if (nextBlockResult?.Spawns?.Count is > 0)
                            continue;
                    }
                    else
                    {
                        int newfaildepth = 0;

                        if (!MatchBlock(testid, brahchNextIndex.Value, nextBranch.nextBlock, testBlocks, blocks, false, depth + 1, ref newfaildepth))
                        {
                            if (newfaildepth > maxFailDepth)
                            {
                                maxFailDepth = newfaildepth;
                                maxFailIndex = j;
                            }
                            continue;
                        }
                    }

                    foundMatchingBranch = j;
                    break;
                }

                if (foundMatchingBranch is not null)
                {
                    remainingBranches.RemoveAt(foundMatchingBranch.Value);
                }
                else
                {
                    if (maxFailIndex is null || brahchNextIndex is null)
                    {
                        faildepth = depth;
                        if (report)
                            Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} result {resultindex}) Failed to match test branch {i}");
                        return false;
                    }
                    else
                    {
                        var n = remainingBranches[maxFailIndex.Value];
                        if (report)
                            MatchBlock(testid, brahchNextIndex.Value, n.nextBlock, testBlocks, blocks, true, depth + 1, ref faildepth);
                        faildepth = depth;
                        return false;
                    }
                }
            }

            return true;
        }

        bool ok = true;
        int startTest = specificTest ?? 1;
        for (int i = startTest; specificTest is null || i == specificTest; i++)
        {
            MethodInfo? testMethod = TestMethods.GetTestMethodInfo(i);
            if (testMethod is null)
            {
                break;
            }

            SpawnAnNPCRewriteData d;
            try
            {
                d = SpawnAnNPCRewriter.RewriteMethod(testMethod, false, ilprintout);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Self-test {i} fail at rewrite: {e}");
                ok = false;
                continue;
            }

#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var spawner = (NPC.Spawner)FormatterServices.GetSafeUninitializedObject(typeof(NPC.Spawner));
#pragma warning restore SYSLIB0050 // Type or member is obsolete

            SpawnAnNpcStaticData staticData = new()
            {
                spawner = spawner,
                spawnTileX = 100,
                spawnTileY = 100,
                spawnTileType = TileID.Grass,
                spawnWallType = WallID.None,
                xRange = false,
                target = 0,
                chances = SpawnerChances.WithValuesFrom(spawner)
            };

            Simulator sim = new(
                staticData,
                d.Method,
                d.Nodes,
                d.LocalStateType,
                SpawnAnNpcTimelineState.StateType,
                () => new SpawnAnNpcBlockResult()
            );

            TestMethods.PrepareSimulationForTest(i, sim);

            SimulationResult simulationResult;

            try
            {
                simulationResult = sim.Simulate();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Self-test {i} fail at simulation: {e}");
                ok = false;
                continue;
            }

            if (printNodes)
            {
                for (int j = 0; j < simulationResult.blocks.Length; j++)
                {
                    var block = simulationResult.blocks[j];
                    var result = block.results as SpawnAnNpcBlockResult;

                    Console.WriteLine($"Block {j}:");

                    if (result?.Spawns is not null)
                    {
                        foreach (var spawn in result.Spawns)
                        {
                            Console.WriteLine($"  Spawn {NPCID.Search.GetName(spawn.npcId)} [{spawn.npcId}] @ {spawn.pos}");
                        }
                    }

                    foreach (var branch in block.nextBranches)
                    {
                        Console.WriteLine($"  Branch {branch.chance * 100:.0}% -> block {branch.nextBlock}");
                    }
                }
            }

            /*
            if (printChances)
            {
                AnalyzedSpawns spawnChances = SpawnNodeAnalyzer.Analyze(simulationResult);

                Console.WriteLine("Calculated chances:");
                foreach (var mspawn in spawnChances.npcSpawns.Values)
                {
                    Console.Write($"  {NPCID.Search.GetName(mspawn.id)} [{mspawn.id}]: ");
                    bool firstPos = true;
                    foreach (var posSpawn in mspawn.spawns)
                    {
                        if (!firstPos)
                            Console.Write(", ");

                        firstPos = false;
                        Console.Write($" [ ");

                        bool firstSpawn = true;
                        foreach (var (pos, spawn) in posSpawn.Iter())
                        {
                            if (!firstSpawn)
                                Console.Write(", ");

                            firstSpawn = false;

                            Console.Write($"{spawn.chance * 100:0.0}% ");

                            if (pos is null)
                                Console.Write($"no pos");
                            else
                                Console.Write($"at {pos.Value}");

                            if (spawn.affectedByLuck)
                                Console.Write(" (luck)");
                            if (spawn.leaked)
                                Console.Write(" (leak)");
                        }
                        Console.Write(" ]");
                    }
                    Console.WriteLine();
                }
            }
            */

            TestMethods.TestBlock[]? testData = TestMethods.GetExpectedTestResults(i);

            if (testData is null)
            {
                Console.WriteLine($"Self-test {i} ran, no test data to verify results");
                continue;
            }


            int faildepth = 0;
            if (!MatchBlock(i, 0, 0, testData, simulationResult.blocks, true, 0, ref faildepth))
            {
                ok = false;
                continue;
            }

            Console.WriteLine($"Self-test {i} pass");
        }

        return ok;
    }

    delegate void SpawnAnNPC(int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target);

    public static void BeginAnalyze(Player player)
    {
        var impl = GetDefaultImplBlocking();
        if (impl is null)
            return;

        LastAnalysis = new(player, impl);
        SpawnAnalyzerUI.SendEvent(new NewSpawnAnalysisEvent(LastAnalysis));
    }

    public static void ClearAnalysis()
    {
        LastAnalysis = null;
        SpawnAnalyzerUI.SendEvent(new NewSpawnAnalysisEvent(null));
    }

    delegate void orig_Main_Update(Main self, GameTime time);
    static void On_Main_Update(orig_Main_Update orig, Main self, GameTime time)
    {
        orig(self, time);

        if (!Main.gameMenu)
        {
            if (DebugCountdown == 0)
            {
                BeginAnalyze(Main.player[Main.myPlayer]);
                SpawnAnalyzerUI.Open();
            }
            if (DebugCountdown >= 0)
                DebugCountdown--;
        }
    }

    delegate void orig_Main_UpdateUIStates(GameTime time);
    static void On_Main_UpdateUIStates(orig_Main_UpdateUIStates orig, GameTime time)
    {
        orig(time);

        SpawnAnalyzerUI.UpdateUI(time);
    }

    delegate void orig_Main_DrawMouseOver(Main self);
    static void On_Main_DrawMouseOver(orig_Main_DrawMouseOver orig, Main self)
    {
        LastAnalysis?.MouseOver();

        orig(self);
    }

    delegate void orig_Main_SetupDrawInterfaceLayers(Main self);
    static void On_Main_SetupDrawInterfaceLayers(orig_Main_SetupDrawInterfaceLayers orig, Main self)
    {
        orig(self);

        List<GameInterfaceLayer> layers = (List<GameInterfaceLayer>)Utils.GetFieldOrThrow<Main>("_gameInterfaceLayers").GetValue(self)!;

        layers.Insert(0, new LegacyGameInterfaceLayer("SpawnAnalyzer: Overlay", delegate
        {
            LastAnalysis?.DrawOverlay(Main.spriteBatch);
            return true;
        }));

        int inventoryIndex = layers.FindIndex(l => l.Name == "Vanilla: Inventory");
        layers.Insert(inventoryIndex + 1, new LegacyGameInterfaceLayer("SpawnAnalyzer: Overlay", delegate
        {
            SpawnAnalyzerUI.DrawLayer(Main.spriteBatch);
            return true;
        }, InterfaceScaleType.UI));
    }

    delegate NPC orig_NPC_Spawner_SpawnNPC(NPC.Spawner self, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target);
    static NPC On_NPC_Spawner_SpawnNPC(orig_NPC_Spawner_SpawnNPC orig, NPC.Spawner self, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target)
    {
        if (Simulator.CurrentlyRunningSimulation is { staticSimData: SpawnAnNpcStaticData staticData } sim && ReferenceEquals(staticData.spawner, self))
        {
            sim.NonDeterministic = true;
            ((SpawnAnNpcBlockResult)sim.GetCurrentBlockRunResult()).AddCurrentConnectionSpawn(new()
            {
                pos = new(X, Y),
                npcId = Type,
                leakedSpawn = true,
            });
            return new() { whoAmI = 199 };
        }

        return orig(self, X, Y, Type, Start, ai0, ai1, ai2, ai3, Target);
    }

    delegate int orig_NPC_NewNPC(IEntitySource source, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target);
    static int On_NPC_NewNPC(orig_NPC_NewNPC orig, IEntitySource source, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target)
    {
        if (Simulator.CurrentlyRunningSimulation is { staticSimData: SpawnAnNpcStaticData } sim)
        {
            sim.NonDeterministic = true;
            ((SpawnAnNpcBlockResult)sim.GetCurrentBlockRunResult()).AddCurrentConnectionSpawn(new()
            {
                pos = new(X, Y),
                npcId = Type,
                leakedSpawn = true,
            });
            return Main.maxNPCs;
        }

        return orig(source, X, Y, Type, Start, ai0, ai1, ai2, ai3, Target);
    }

    static void IL_UnifiedRandom_InternalSample(ILContext il)
    {
        static void RandomCalledOnSimulation(Simulator ctx)
        {
            ctx.NonDeterministic = true;

            // Maybe report it in the console?
        }

        Action<Simulator> RandomCalledOnSimulationDelegate = RandomCalledOnSimulation;
        MethodInfo RandomCalledOnSimulationMethod = RandomCalledOnSimulationDelegate.Method;

        ILCursor c = new(il);

        ILLabel realCode = c.DefineLabel();

        ILLabel contextHook = c.DefineLabel();

        PropertyInfo prop = Utils.GetPropertyOrThrow(typeof(Simulator), nameof(Simulator.CurrentlyRunningSimulation));

        // Quickly call and jump to real code when null
        c.Emit(OpCodes.Call, prop.GetMethod!);
        c.Emit(OpCodes.Brfalse, realCode);

        // Not null on the first try, get the actual value
        c.Emit(OpCodes.Call, prop.GetMethod!);
        c.Emit(OpCodes.Dup);

        // But what if it's suddenly null?
        c.Emit(OpCodes.Brtrue, contextHook);
        c.Emit(OpCodes.Pop);
        c.Emit(OpCodes.Br, realCode);

        c.MarkLabel(contextHook);

        c.Emit(OpCodes.Call, RandomCalledOnSimulationMethod);

        c.MarkLabel(realCode);
    }

    internal static bool MatchInstructions(ILContext c, int pos, out int matchEndPos, params Func<Instruction, bool>[] matchers)
    {
        matchEndPos = pos;
        for (int i = 0; i < matchers.Length; i++)
        {
            matchEndPos = pos + i;
            if (pos + i >= c.Instrs.Count)
            {
                return false;
            }
            if (!matchers[i](c.Instrs[pos + i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static float PredictLuckChanceMod(float luck)
    {

        if (luck < 0.0f)
        {
            return 0.3f * Math.Max(-1.0f, luck) + 1.0f;
        }
        else if (luck > 0.0f)
        {
            return 0.4f * Math.Min(luck, 1.0f) + 1.0f;
        }
        else
        {
            return 1.0f;
        }
    }

    internal static float PredictBadLuckExtremeChanceMod(float luck)
    {
        if (luck < 0.0f)
        {
            return -9.0f * Math.Max(-1.0f, luck) + 1.0f;
        }
        else if (luck > 0.0f)
        {
            return -0.9f * Math.Min(luck, 1.0f) + 1.0f;
        }
        else
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// Average multiplier of
    /// <code>
    /// if (rand(chance)) 
    ///     value *= multiplier
    /// </code>
    /// </summary>
    internal static float PredictAverageRandomChanceMultiplier(float chance, float multiplier)
    {
        return (1.0f - chance) + (multiplier * chance);
    }

    /// <summary>
    /// Returns the chance for any of the two hitting
    /// </summary>
    internal static float CombineChances(float a, float b)
    {
        if (a <= 0)
        {
            return b;
        }
        if (b <= 0)
        {
            return a;
        }
        if (a >= 1.0 || b >= 1.0)
        {
            return 1.0f;
        }
        return a + b - (a * b);
    }

    internal static float PredictChooseWithAttemptsChanceMultiplier(float chancesSum, int attempts)
    {
        float remaining = 1 - chancesSum;
        if (remaining <= 0)
            return 1;

        return (float)((1 - Math.Pow((double)remaining, attempts)) / chancesSum);
    }

    internal static void ReportUnknownPattern(string type, ILContext c, int index, int showBefore, int showAfter)
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
}
