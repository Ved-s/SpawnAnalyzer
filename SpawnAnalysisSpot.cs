using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria;
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
        
        SpawnAnalyzer.AnalyzeSimulationResults(results.Value.nodes, results.Value.startNode, 0, v =>
        {
            var (spawn, rollParams, chance) = v;

            int x = spawn.x / 16;
            int y = spawn.y / 16;

            Point pos = new(x, y);

            if (!analysis.results.TryGetValue(pos, out SpawnAnalysisResult? result))
            {
                result = new();
                analysis.results.Add(pos, result);
            }

            if (!result.spawns.TryGetValue(spawn.npcId, out SpawnAnalysisResultSpawn? resultSpawn))
            {
                resultSpawn = new(spawn.npcId);
                result.spawns.Add(spawn.npcId, resultSpawn);
            }

            if (spawn.leakedSpawn)
            {
                resultSpawn.leakedSpawnChance += chance;
                resultSpawn.leakedSpawnCount++;
            }
            else
            {
                resultSpawn.chance += chance;
                resultSpawn.count++;
            }

            resultSpawn.dependsOnLuck |= rollParams.dependsOnLuck;
        });
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