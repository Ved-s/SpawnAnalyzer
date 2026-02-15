using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SpawnAnalyzer.Rewriters;
using SpawnAnalyzer.Rewriters.SpawnANnNPC;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer;

// TODO: nodes for inputs with chances
// TODO: no side effects in the simulated function
// TODO: selftest on test methods

// TODO: nodes for NPCCount

// TODO: build method block tree to determine when locals end
// TODO: Rules for chances depending on other chances, end of SetSpawnFlagsForChosenTile
public class SpawnAnalyzer
{
    static SpawnAnalysis? LastAnalysis;

    internal delegate bool GetSpawnTileParams(NPC.Spawner spawner, Player player, ref int x, ref int y, Rectangle spawnArea, Rectangle safeArea, out SpawnParamsStage1 spawnParams);
    internal static readonly GetSpawnTileParams GetSpawnTileParamsImpl = GetSpawnTileParamsRewriter.GenerateMethod();

    internal delegate void SetSpawnFlagsForChosenTile(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, ref SpawnParamsStage2 spawnParams);
    internal static readonly SetSpawnFlagsForChosenTile SetSpawnFlagsForChosenTileImpl = SetSpawnFlagsForChosenTileRewriter.GenerateMethod();

    internal static Hook? MainUpdateHook;
    internal static Hook? MainDrawMouseOverHook;
    internal static Hook? MainSetupDrawInterfaceLayersHook;

    static int AnalyzeInTicks = -1;
    public static void InstallVanilla()
    {
        Player.Hooks.OnEnterWorld += (_) => AnalyzeInTicks = 10;

        MainUpdateHook                   = new Hook(Utils.GetMethodOrThrow<Main>("Update"), On_Main_Update);
        MainDrawMouseOverHook            = new Hook(Utils.GetMethodOrThrow<Main>("DrawMouseOver"), On_Main_DrawMouseOver);
        MainSetupDrawInterfaceLayersHook = new Hook(Utils.GetMethodOrThrow<Main>("SetupDrawInterfaceLayers"), On_Main_SetupDrawInterfaceLayers);
        
        Stopwatch sw = Stopwatch.StartNew();
        var d = SpawnAnNPCRewriter.RewriteMethod(null, false); //TestMethods.GetTestMethodInfo(9), false);
        sw.Stop();
        Console.WriteLine($"Rewrote method in {sw.ElapsedMilliseconds}ms");

#pragma warning disable SYSLIB0050 // Type or member is obsolete
        var spawner = (NPC.Spawner)FormatterServices.GetSafeUninitializedObject(typeof(NPC.Spawner));
#pragma warning restore SYSLIB0050 // Type or member is obsolete

        int x = 100;
        int y = 100;
        int tileType = TileID.Grass;

        var ctx = new SpawnSimulationContext(d, spawner, x, y, tileType, false);

        Main.tile = new Tile[500, 500];

        for (int i = 0; i < Main.tile.GetLength(0); i++)
        {
            for (int j = 0; j < Main.tile.GetLength(1); j++)
            {
                Main.tile[i, j] = new();
            }
        }

        Main.tile[x, y].type = (ushort)tileType;

        Main.npc = new NPC[200];
        for (int i = 0; i < Main.npc.Length; i++)
        {
            Main.npc[i] = new();
        }

        Main.ActiveWorldFileData = new();
        Main.ActiveWorldFileData.WorldId = 1;
        NPC.SetWorldSpecificMonstersByWorldID();

        spawner.dayTime = true;
        spawner.surfaceSpawn = true;


        sw.Restart();
        var data = ctx.Simulate() ?? throw new NullReferenceException();
        sw.Stop();

        Console.WriteLine($"Simulated in {sw.ElapsedMilliseconds}ms, entry node {data.startNode}, visited {data.nodes.Count(n => n is not null)}/{d.Nodes.Length} nodes");

        for (int i = 0; i < data.nodes.Count; i++)
        {
            var node = data.nodes[i];
            if (node is null)
            {
                continue;
            }

            int offset = d.Nodes[i].Offset;
            if (data.startNode == i)
            {
                Console.WriteLine($"Node {i} [IL_{offset:x4}] (start): ");
            }
            else
            {
                Console.WriteLine($"Node {i} [IL_{offset:x4}]: ");
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
                                for (int j = 0; j < ps.Length; j++)
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
                            for (int j = 0; j < ps.Length; j++)
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

        Dictionary<int, (NodeRollParams, float)> spawns = new();

        // (node, timeline, branch, chance)
        Stack<(int, int, int, float)> exploreStack = new();

        var startTimeline = data.nodes[data.startNode]!.timelines[0];

        for (int i = 0; i < startTimeline.branches.Length; i++)
        {
            exploreStack.Push((data.startNode, 0, i, startTimeline.branches[i].info.chance));
        }

        void MergeParams(NodeRollParams into, NodeRollParams p)
        {
            into.dependsOnLuck |= p.dependsOnLuck;
        }

        while (exploreStack.Count > 0)
        {
            var (node, timeline, branch, percent) = exploreStack.Pop();

            var timelinev = data.nodes[node]!.timelines[timeline];
            var branchv = timelinev.branches[branch];

            (NodeRollParams, float) oldvalue;
            if (branchv.spawns is not null)
            {
                foreach (var spawn in branchv.spawns)
                {
                    if (!spawns.TryGetValue(spawn.npcId, out oldvalue))
                    {
                        oldvalue = (new(), 0);
                    }

                    MergeParams(oldvalue.Item1, timelinev.rollParams);
                    spawns[spawn.npcId] = (oldvalue.Item1, percent + oldvalue.Item2);
                }
            }

            switch (branchv.ConnectionType)
            {
                case NodeConnectionType.RandomNode:
                    var next = branchv.nextNode!;
                    var nextTimeline = data.nodes[next.node]!.timelines[next.timeline];
                    for (int i = 0; i < nextTimeline.branches.Length; i++)
                    {
                        exploreStack.Push((next.node, next.timeline, i, nextTimeline.branches[i].info.chance * percent));
                    }
                    break;

                case NodeConnectionType.SpawnNode:
                    break;

                case NodeConnectionType.NoConnection:
                    if (!spawns.TryGetValue(int.MinValue, out oldvalue))
                    {
                        oldvalue = (new(), 0);
                    }

                    spawns[int.MinValue] = (oldvalue.Item1, percent + oldvalue.Item2);
                    break;
            }
        }

        Console.WriteLine($"Luck: {spawner.luck:0.00}");

        Console.WriteLine("Calculated spawns:");
        float chancesAdd = 0;

        foreach (KeyValuePair<int, (NodeRollParams, float)> kvp in spawns.OrderBy(kvp => kvp.Value.Item2))
        {
            chancesAdd += kvp.Value.Item2;
            if (kvp.Key == int.MinValue)
                Console.WriteLine($" Nothing: {kvp.Value.Item2 * 100:.000}%");
            else
            {
                Console.Write($" {NPCID.Search.GetName(kvp.Key)} [{kvp.Key}]: {kvp.Value.Item2 * 100:.000}%");

                NodeRollParams rp = kvp.Value.Item1;

                if (rp.dependsOnLuck)
                {
                    Console.Write(" [luck]");
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine($"Chances add up to {chancesAdd * 100:0.000}%\n");

        Environment.Exit(1);
    }

    delegate void SpawnAnNPC(int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target);

    static void BeginAnalyze(Player player)
    {
        Stopwatch sw = Stopwatch.StartNew();
        LastAnalysis = new(player);
        sw.Stop();
        Console.WriteLine($"Analyzed spawns in {sw.ElapsedMilliseconds}ms");
    }

    delegate void orig_Main_Update(Main self, GameTime time);
    static void On_Main_Update(orig_Main_Update orig, Main self, GameTime time)
    {
        orig(self, time);

        if (AnalyzeInTicks == 0 || (Main.keyState.IsKeyDown(Keys.Z) && !Main.oldKeyState.IsKeyDown(Keys.Z)))
        {
            BeginAnalyze(Main.player[Main.myPlayer]);
        }

        if (AnalyzeInTicks >= 0)
        {
            AnalyzeInTicks -= 1;
        }
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
        layers.Add(new LegacyGameInterfaceLayer("SpawnAnalyzer: Overlay", delegate
        {
            LastAnalysis?.DrawOverlay(Main.spriteBatch);
            return true;
        }));
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