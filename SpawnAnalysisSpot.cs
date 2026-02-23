using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.ID;
using Terraria.Map;

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

    public SpawnAnalysisSpot(SpawnAnalysis analysis, Point position, SpawnParamsStage1 p)
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

        SpawnAnalyzer.SetSpawnFlagsForChosenTileImpl(localSpawner, this.position.X, this.position.Y, spawnTileType, spawnWallType, localChances);

        chances = localChances;

        simulationContext = new(SpawnAnalyzer.SpawnAnNpcRewrite, chances, localSpawner, this.position.X, this.position.Y, spawnTileType, xRange);
    }

    internal void Simulate()
    {
        SimulationResult? results = simulationContext.Simulate();
        if (results is null)
            return;

        AnalyzedSpawns spawnresults = SpawnNodeAnalyzer.Analyze(results.Value);

        Dictionary<AnalyzedMultiSpawn, int> nextPosDict = new();

        Dictionary<Point, List<AnalyzedSpawn>> spawnsByTile = new();

        foreach (var mspawn in spawnresults.spawns.Values)
        {
            nextPosDict.Clear();
            spawnsByTile.Clear();

            foreach (var spawn in mspawn.spawns)
            {
                Point pos = spawn.TileWorldPos;
                if (spawnsByTile.TryGetValue(pos, out var existingTileSpawns))
                {
                    bool merged = false;
                    foreach (var ess in existingTileSpawns)
                        if (ess.pixelWorldPos == spawn.pixelWorldPos)
                        {
                            ess.MergeFrom(spawn);
                            merged = true;
                            break;
                        }
                    if (merged)
                        continue;
                }
                else
                {
                    existingTileSpawns = new();
                    spawnsByTile.Add(pos, existingTileSpawns);
                }

                existingTileSpawns.Add(spawn);

                if (!analysis.results.TryGetValue(pos, out var posDict))
                {
                    posDict = new();
                    analysis.results.Add(pos, posDict);
                }

                if (!posDict.TryGetValue(mspawn.id, out var spawnres))
                {
                    spawnres = new(mspawn.id);
                    posDict.Add(mspawn.id, spawnres);
                }

                if (!nextPosDict.TryGetValue(mspawn, out int index))
                {
                    index = 0;
                }

                if (index >= spawnres.spawns.Count)
                    spawnres.spawns.Add(spawn);
                else
                    spawnres.spawns[index].MergeFrom(spawn);

                nextPosDict[mspawn] = index + 1;
            }
        }
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