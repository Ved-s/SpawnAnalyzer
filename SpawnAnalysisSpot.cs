using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public int hits = 1;

    readonly bool xRange;

    readonly SpawnerChances chances;

    readonly NPC.Spawner localSpawner;
    readonly SpawnAnalysis analysis;

    readonly SpawnSimulationContext simulationContext;

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

    internal void Simulate(out TimeSpan analysisTime)
    {
        SimulationResult? results = simulationContext.Simulate();
        analysisTime = default;
        if (results is null)
            return;

        Stopwatch sw = Stopwatch.StartNew();
        AnalyzedSpawns spawnresults = SpawnNodeAnalyzer.Analyze(results.Value);

        foreach (var mspawn in spawnresults.spawns.Values)
        {
            mspawn.ConvertToTilePos();
            foreach (var posSpawn in mspawn.spawns)
            {
                if (!analysis.results.TryGetValue(posSpawn.Key, out var posDict))
                {
                    posDict = new();
                    analysis.results.Add(posSpawn.Key, posDict);
                }

                if (!posDict.TryGetValue(mspawn.id, out var spawnres))
                {
                    spawnres = new(mspawn.id);
                    posDict.Add(mspawn.id, spawnres);
                }

                spawnres.MergeFrom(posSpawn.Value);
            }
        }
        sw.Stop();
        analysisTime = sw.Elapsed;
    }

    internal void MouseOver(StringBuilder mouseOverText)
    {
        mouseOverText.Append($"Mob spawn spot ");

        int spawnAreaArea = analysis.spawnArea.Width * analysis.spawnArea.Height;
        double chancePercent = (double)hits / spawnAreaArea * 100;

        mouseOverText.AppendLine($"({chancePercent:0.00}% to be picked to spawn)");

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