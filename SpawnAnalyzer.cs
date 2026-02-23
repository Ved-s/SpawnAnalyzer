using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SpawnAnalyzer.Rewriters;
using SpawnAnalyzer.Rewriters.SpawnANnNPC;
using SpawnAnalyzer.Simulation;
using SpawnAnalyzer.UI;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer;

// TODO: fix forst rolled dragonfly type being misplaced
// TODO: Optimize 100% and 0% chances, merge same return value branches

// TODO: warning about side-effects and inconsistent chances
// TODO: no side effects in the simulated function
// TODO: Spawner.ShouldSpawnInvasionEnemies in SetSpawnFlags

// TODO: nodes for NPCCount
// TODO: better selftests?
// TODO: slime code at the start of SpawnNPC
// TODO: anything that can go wrong, will go wrong, show warnings and errors

// TODO: build method block tree to determine when locals end
// TODO: Rules for chances depending on other chances, end of SetSpawnFlagsForChosenTile
public class SpawnAnalyzer
{
    public static SpawnAnalysis? LastAnalysis;

    internal delegate bool GetSpawnTileParams(NPC.Spawner spawner, Player player, ref int x, ref int y, Rectangle spawnArea, Rectangle safeArea, out SpawnParamsStage1 spawnParams);
    internal static readonly GetSpawnTileParams GetSpawnTileParamsImpl = GetSpawnTileParamsRewriter.GenerateMethod();

    internal delegate void SetSpawnFlagsForChosenTile(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, SpawnerChances spawnParams);
    internal static readonly SetSpawnFlagsForChosenTile SetSpawnFlagsForChosenTileImpl = SetSpawnFlagsForChosenTileRewriter.GenerateMethod();

    internal delegate void GetSpawnRate(NPC.Spawner spawner, Player player, out int spawnRate, out int maxSpawns, SpawnerChances spawnParams);
    internal static readonly GetSpawnRate GetSpawnRateImpl = GetSpawnRateRewriter.GenerateMethod();

    // TODO: offload to a different thread
    internal static readonly SpawnAnNPCRewriteData SpawnAnNpcRewrite = SpawnAnNPCRewriter.RewriteMethod(null);

    internal static Hook? MainUpdateHook;
    internal static Hook? MainUpdateUIStatesHook;
    internal static Hook? MainDrawMouseOverHook;
    internal static Hook? MainSetupDrawInterfaceLayersHook;
    internal static Hook? NPCSpawnerSpawnNPCHook;
    internal static Hook? NPCNewNPCHook;

    static Dictionary<string, Texture2D> TextureCache = new();

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
        bool MatchNode(int testid, int testindex, int simindex, int simtimeline, TestMethods.TestNode[] testNodes, List<SimulationNode?> simNodes, bool report, int depth, ref int faildepth)
        {
            TestMethods.TestNode testnode = testNodes[testindex];
            SimulationNode? simnode = simNodes[simindex];

            if (simnode is null)
            {
                faildepth = depth;
                if (report)
                    Console.WriteLine($"Self-test {testid} fail at results: Expected simulation node {simindex} to be populated");
                return false;
            }

            var branches = simnode.timelines[simtimeline].branches.ToList();

            if (branches.Count != testnode.branches.Length)
            {
                faildepth = depth;
                if (report)
                    Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} sim {simindex}/{simtimeline}) Expected {testnode.branches.Length} branches, got {branches.Count}");
                return false;
            }

            for (int i = 0; i < testnode.branches.Length; i++)
            {
                (float, int[], int?) branch = testnode.branches[i];

                int? foundIndex = null;

                int maxFailDepth = 0;
                int? maxFailIndex = null;

                for (int j = 0; j < branches.Count; j++)
                {
                    var n = branches[j];

                    if (branch.Item1 - 0.005f > n.info.chance || branch.Item1 + 0.005f < n.info.chance)
                        continue;

                    if (branch.Item2.Length > 0)
                    {
                        if ((n.spawns?.Count ?? 0) != branch.Item2.Length)
                        {
                            continue;
                        }

                        bool spawnsOk = true;
                        foreach (var (a, b) in n.spawns!.Zip(branch.Item2))
                        {
                            if (a.npcId != b)
                            {
                                spawnsOk = false;
                                break;
                            }
                        }
                        if (!spawnsOk)
                            continue;
                    }

                    if (branch.Item3 is not null)
                    {
                        if (n.nextNode is null)
                        {
                            continue;
                        }

                        int newfaildepth = 0;

                        if (!MatchNode(testid, branch.Item3.Value, n.nextNode.node, n.nextNode.timeline, testNodes, simNodes, false, depth + 1, ref newfaildepth))
                        {
                            if (newfaildepth > maxFailDepth)
                            {
                                maxFailDepth = newfaildepth;
                                maxFailIndex = j;
                            }
                            continue;
                        }
                    }

                    foundIndex = j;
                    break;
                }

                if (foundIndex is not null)
                {
                    branches.RemoveAt(foundIndex.Value);
                }
                else
                {
                    if (maxFailIndex is null)
                    {
                        faildepth = depth;
                        if (report)
                            Console.WriteLine($"Self-test {testid} fail at results: Node (test {testindex} sim {simindex}/{simtimeline}) Failed to match test branch {i}");
                        return false;
                    }
                    else
                    {
                        var n = branches[maxFailIndex.Value];
                        if (report)
                            MatchNode(testid, branch.Item3!.Value, n.nextNode!.node, n.nextNode.timeline, testNodes, simNodes, true, depth + 1, ref faildepth);
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

            int x = 100;
            int y = 100;
            int tileType = TileID.Grass;

            SpawnerChances chances = SpawnerChances.WithValuesFrom(spawner);

            SpawnSimulationContext ctx = new SpawnSimulationContext(d, chances, spawner, x, y, tileType, false);

            TestMethods.PrepareSimulationForTest(i, ctx);

            SimulationResult simulationResult;

            try
            {
                simulationResult = ctx.Simulate() ?? throw new NullReferenceException("Simulate returned null");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Self-test {i} fail at simulation: {e}");
                ok = false;
                continue;
            }

            if (printNodes)
            {
                for (int j = 0; j < simulationResult.nodes.Count; j++)
                {
                    var node = simulationResult.nodes[j];
                    if (node is null)
                    {
                        continue;
                    }

                    SimulationNodeInfo nodeInfo = d.Nodes[j];
                    if (simulationResult.startNode == j)
                    {
                        Console.WriteLine($"Node {j} [{nodeInfo.node.GetType().Name} at IL_{nodeInfo.Offset:x4}] (start): ");
                    }
                    else
                    {
                        Console.WriteLine($"Node {j} [{nodeInfo.node.GetType().Name} at IL_{nodeInfo.Offset:x4}]: ");
                    }

                    for (int t = 0; t < node.timelines.Count; t++)
                    {

                        Console.WriteLine($"  Timeline {t}:");
                        var timeline = node.timelines[t];
                        foreach (var branch in timeline.branches)
                        {
                            string ps = $"   [{branch.info.chance * 100:.0}%] -> ";
                            Console.Write(ps);

                            bool firstline = true;

                            if (branch.spawns is not null)
                            {
                                foreach (var spawn in branch.spawns)
                                {
                                    if (!firstline)
                                    {
                                        Console.WriteLine(",");
                                        for (int k = 0; k < ps.Length; k++)
                                        {
                                            Console.Write(' ');
                                        }
                                    }
                                    firstline = false;
                                    Console.Write($"Spawn {NPCID.Search.GetName(spawn.npcId)} [{spawn.npcId}] @ {spawn.x}, {spawn.y}");
                                }
                            }

                            if (branch.nextNode is not null)
                            {
                                if (!firstline)
                                {
                                    Console.WriteLine(",");
                                    for (int k = 0; k < ps.Length; k++)
                                    {
                                        Console.Write(' ');
                                    }
                                }
                                firstline = false;

                                Console.Write($"Node {branch.nextNode!.node}/{branch.nextNode!.timeline}");
                            }

                            if (firstline)
                            {
                                Console.Write($"Nothing");
                            }
                            Console.WriteLine();
                        }
                    }
                }
            }

            if (printChances)
            {
                AnalyzedSpawns spawnChances = SpawnNodeAnalyzer.Analyze(simulationResult);

                Console.WriteLine("Calculated chances:");
                foreach (var mspawn in spawnChances.spawns.Values.OrderByDescending(s => s.spawns[0].chance))
                {
                    Console.Write($"  {NPCID.Search.GetName(mspawn.id)} [{mspawn.id}]: ");
                    for (int i1 = 0; i1 < mspawn.spawns.Count; i1++)
                    {
                        AnalyzedSpawn spawn = mspawn.spawns[i1];

                        if (i1 > 0)
                            Console.Write(", ");
                        
                        Console.Write($"{spawn.chance * 100:0.0}% at {spawn.pixelWorldPos}");
                        if (spawn.affectedByLuck)
                            Console.Write($" [luck]");
                        if (spawn.leaked)
                            Console.Write($" [leak]");
                    }
                    Console.WriteLine();
                }
            }

            TestMethods.TestNode[]? testData = TestMethods.GetExpectedTestResults(i);

            if (testData is null)
            {
                Console.WriteLine($"Self-test {i} ran, no test data to verify results");
                continue;
            }


            int faildepth = 0;
            if (!MatchNode(i, 0, simulationResult.startNode, 0, testData, simulationResult.nodes, true, 0, ref faildepth))
            {
                ok = false;
                continue;
            }

            Console.WriteLine($"Self-test {i} pass");
        }

        return ok;
    }

    delegate void SpawnAnNPC(int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target);

    static void BeginAnalyze(Player player)
    {
        LastAnalysis = new(player);
    }

    delegate void orig_Main_Update(Main self, GameTime time);
    static void On_Main_Update(orig_Main_Update orig, Main self, GameTime time)
    {
        orig(self, time);

        if (Main.keyState.IsKeyDown(Keys.Z) && !Main.oldKeyState.IsKeyDown(Keys.Z))
        {
            if (Main.keyState.PressingShift())
            {
                LastAnalysis?.Simulate();
            }
            else
            {
                BeginAnalyze(Main.player[Main.myPlayer]);
            }
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
        layers.Insert(inventoryIndex+1, new LegacyGameInterfaceLayer("SpawnAnalyzer: Overlay", delegate
        {
            SpawnAnalyzerUI.DrawLayer(Main.spriteBatch);
            return true;
        }, InterfaceScaleType.UI));
    }

    delegate NPC orig_NPC_Spawner_SpawnNPC(NPC.Spawner self, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target);
    static NPC On_NPC_Spawner_SpawnNPC(orig_NPC_Spawner_SpawnNPC orig, NPC.Spawner self, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target)
    {
        if (SpawnSimulationContext.CurrentlySimulatingContext is not null)
        {
            SpawnSimulationContext.CurrentlySimulatingContext.AddCurrentConnectionSpawn(new()
            {
                x = X,
                y = Y,
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
        if (SpawnSimulationContext.CurrentlySimulatingContext is not null)
        {
            SpawnSimulationContext.CurrentlySimulatingContext.AddCurrentConnectionSpawn(new()
            {
                x = X,
                y = Y,
                npcId = Type,
                leakedSpawn = true,
            });
            return Main.maxNPCs;
        }

        return orig(source, X, Y, Type, Start, ai0, ai1, ai2, ai3, Target);
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