using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    public Point spawnPosition;
    public Point tilePosition;

    readonly int spawnTileType;
    readonly int spawnWallType;

    public ulong hits = 1;
    public float chance = 0;

    public readonly float validChance;

    readonly bool xRange;

    readonly SpawnerChances chances;

    readonly NPC.Spawner localSpawner;
    readonly SpawnAnalysis analysis;

    readonly SpawnSimulationContext simulationContext;

    public bool NonDeterministic = false;

    private SpawnAnalysisSpot(
        SpawnerChances chances,
        NPC.Spawner localSpawner,
        SpawnAnalysis analysis,
        SpawnSimulationContext simulationContext,
        SpawnParamsStage1 p,
        Point spawnPosition,
        Point tilePosition,
        int spawnTileType,
        int spawnWallType,
        float validChance
    )
    {
        this.chances = chances;
        this.localSpawner = localSpawner;
        this.analysis = analysis;
        this.simulationContext = simulationContext;

        localSpawner.skyMob = p.skyMob;
        xRange = p.xRange;
        this.spawnPosition = spawnPosition;
        this.tilePosition = tilePosition;
        this.spawnTileType = spawnTileType;
        this.spawnWallType = spawnWallType;
        this.validChance = validChance;
    }

    public static bool TryConstructSpawnSpot(
        SpawnAnalysis analysis, Point position, SpawnParamsStage1 p, SimulatorImpl impl,
        [NotNullWhen(true)] out SpawnAnalysisSpot? spot
    )
    {
        Point spawnPosition = position;

        NPC.Spawner.FindGroundTile(position.X, position.Y, out int groundY);

        Point tilePosition = new(position.X, groundY);

        int spawnTileType = (int)Main.tile[tilePosition.X, tilePosition.Y].type;
        int spawnWallType = NPC.Spawner.GetSpawnWallType(spawnPosition.X, spawnPosition.Y);
        
        float validChance = impl.PostCheckChosenSpawnTileImpl(analysis.globalSpawner, position.X, position.Y, spawnTileType, spawnWallType);
        if (validChance <= 0)
        {
            spot = null;
            return false;
        }

#pragma warning disable SYSLIB0050 // Type or member is obsolete
        var spawner = (NPC.Spawner)FormatterServices.GetSafeUninitializedObject(typeof(NPC.Spawner));
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        ShallowCloneFields(analysis.globalSpawner, spawner);

        SpawnerChances localChances = SpawnerChances.WithValuesFrom(spawner);
        SpawnerChances.CopyGlobalFields(analysis.globalSpawnerChances, localChances);

        impl.SetSpawnFlagsForChosenTileImpl(spawner, spawnPosition.X, spawnPosition.Y, tilePosition.Y, spawnTileType, spawnWallType, localChances);

        SpawnSimulationContext simulationContext = new(impl.SpawnAnNpcRewrite, localChances, spawner, spawnPosition.X, spawnPosition.Y, spawnTileType, spawnWallType, p.xRange);

        spot = new SpawnAnalysisSpot(
            localChances, spawner, analysis, simulationContext, p, 
            spawnPosition, tilePosition, spawnTileType, spawnWallType, validChance
        );
        return true;
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
        outResults.starting[spawnPosition] = allStartSpawns;

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

    static void ShallowCloneFields<T>(T from, T to)
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