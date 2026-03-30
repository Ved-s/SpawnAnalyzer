using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria;

namespace SpawnAnalyzer;

public class SpawnAnalysisSpot
{
    public Point position;

    readonly int spawnTileType;
    readonly int spawnWallType;

    public ulong hits = 1;
    public float chance = 0;

    readonly bool xRange;

    readonly SpawnerChances chances;

    readonly NPC.Spawner localSpawner;
    readonly SpawnAnalysis analysis;

    readonly SpawnSimulationContext simulationContext;

    public bool NonDeterministic = false;

    public SpawnAnalysisSpot(SpawnAnalysis analysis, Point position, SpawnParamsStage1 p, SimulatorImpl impl)
    {
        this.analysis = analysis;
        this.position = position;

        NPC.Spawner.GetProperGroundSpawnTileTypeAndWallType(position.X, position.Y, out spawnTileType, out spawnWallType);
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        var spawner = (NPC.Spawner)FormatterServices.GetSafeUninitializedObject(typeof(NPC.Spawner));
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        ShallowCloneFields(analysis.globalSpawner, spawner);
        localSpawner = spawner;

        localSpawner.skyMob = p.skyMob;
        xRange = p.xRange;

        SpawnerChances localChances = SpawnerChances.WithValuesFrom(localSpawner);
        SpawnerChances.CopyGlobalFields(analysis.globalSpawnerChances, localChances);

        impl.SetSpawnFlagsForChosenTileImpl(localSpawner, this.position.X, this.position.Y, spawnTileType, spawnWallType, localChances);

        chances = localChances;

        simulationContext = new(impl.SpawnAnNpcRewrite, chances, localSpawner, this.position.X, this.position.Y, spawnTileType, xRange);
    }

    internal void Simulate(out TimeSpan analysisTime, SpawnAnalysisResults outResults)
    {
        SimulationResult? results = simulationContext.Simulate();
        NonDeterministic = simulationContext.NonDeterministic;
        analysis.NonDeterministic |= NonDeterministic;

        analysisTime = default;
        if (results is null)
            return;

        Stopwatch sw = Stopwatch.StartNew();
        AnalyzedSpawns spawnresults = SpawnNodeAnalyzer.Analyze(results.Value);

        Dictionary<int, List<AnalyzedSpawn>> allStartSpawns = new();
        outResults.starting[position] = allStartSpawns;

        Dictionary<Point, List<AnalyzedSpawn>> finalPosSpawns = new();
        List<AnalyzedSpawn> localTotalSpawns = new();

        float chanceMultiplier = chance * analysis.spawnRateChanceMultiplier;

        foreach (var (npcid, npcSpawns) in spawnresults.npcSpawns)
        {
            npcSpawns.ConvertToTilePos();

            List<AnalyzedSpawn> startSpawns = new();
            allStartSpawns.Add(npcid, startSpawns);

            if (!outResults.total.TryGetValue(npcid, out var totalSpawns))
            {
                totalSpawns = new();
                outResults.total.Add(npcid, totalSpawns);
            }

            finalPosSpawns.Clear();
            localTotalSpawns.Clear();

            foreach (var spawn in npcSpawns.spawns)
            {
                foreach (var (pos, posSpawn) in spawn.posSpawns)
                {
                    if (!finalPosSpawns.TryGetValue(pos, out var list))
                    {
                        list = new();
                        finalPosSpawns.Add(pos, list);
                    }

                    list.Add(posSpawn);
                }

                var allSpawn = spawn.AllPositionsSpawn();
                startSpawns.Add(allSpawn.Clone());

                allSpawn.chance *= chanceMultiplier;
                localTotalSpawns.Add(allSpawn);
            }

            for (int i = 0; i < localTotalSpawns.Count; i++)
            {
                if (totalSpawns.Count > i)
                {
                    totalSpawns[i].MergeFrom(localTotalSpawns[i]);
                }
                else
                {
                    totalSpawns.Add(localTotalSpawns[i].Clone());
                }
            }

            npcSpawns.MultiplyChance(chanceMultiplier);

            foreach (var (pos, posSpawns) in finalPosSpawns)
            {
                if (!outResults.final.TryGetValue(pos, out var dict))
                {
                    dict = new();
                    outResults.final.Add(pos, dict);
                }

                if (dict.TryGetValue(npcid, out var finalSpawnsId))
                {
                    for (int i = 0; i < posSpawns.Count; i++)
                    {
                        if (finalSpawnsId.Count > i)
                        {
                            finalSpawnsId[i].MergeFrom(posSpawns[i]);
                        }
                        else
                        {
                            finalSpawnsId.Add(posSpawns[i].Clone());
                        }
                    }
                }
                else
                {
                    dict.Add(npcid, posSpawns.Select(s => s.Clone()).ToList());
                }
            }
        }
        sw.Stop();
        analysisTime = sw.Elapsed;
    }

    internal void MouseOver(StringBuilder mouseOverText)
    {
        mouseOverText.Append($"Mob spawn spot ");

        mouseOverText.AppendLine($"({chance * 100:0.00}% to be picked to spawn)");
    }

    void ShallowCloneFields<T>(T from, T to)
    {
        foreach (FieldInfo field in typeof(T).GetFields())
        {
            if (field.IsStatic)
            {
                continue;
            }

            field.SetValue(to, field.GetValue(from));
        }
    }

    public SpawnParamsStage1 GetStage1Params()
    {
        return new()
        {
            skyMob = localSpawner.skyMob,
            xRange = xRange,
        };
    }
}