using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.Map;

namespace SpawnAnalyzer;

class SpawnAnalysisSpot
{
    public Point position;

    readonly int spawnTileType;
    readonly int spawnWallType;

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
                resultSpawn = new();
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
        mouseOverText.Append($"X: {position.X} Y: {position.Y}\n");
        mouseOverText.Append($"TileID: {spawnTileType} WallID: {spawnWallType}\n");

        if (xRange)
        {
            mouseOverText.Append($"  xRange: True\n");
        }
        if (localSpawner.skyMob && analysis.globalSpawner.skyMob)
        {
            mouseOverText.Append($"  skyMob: True\n");
        }

        StringBuilder globalValues = new();
        bool firstLocal = true;

        foreach (FieldInfo field in typeof(NPC.Spawner).GetFields())
        {
            if (field.IsStatic || field.Name == "pX" || field.Name == "pY")
                continue;

            string chanceName = field.Name + "Chance";
            FieldInfo? chanceField = typeof(SpawnerChances).GetField(chanceName, (BindingFlags)(-1));
            if (chanceField is not null)
            {
                float chance = (float)chanceField.GetValue(chances)!;
                if (chance == 0f)
                {
                    continue;
                }

                if (firstLocal)
                {
                    mouseOverText.Append("Local parameters:\n");
                    firstLocal = false;
                }

                mouseOverText.Append("  ");
                mouseOverText.Append(field.Name);
                mouseOverText.Append(": ");
                mouseOverText.Append($"{chance * 100:0.0}");
                mouseOverText.Append("%\n");

                continue;
            }

            object globalValue = field.GetValue(analysis.globalSpawner)!;
            object localValue = field.GetValue(localSpawner)!;
            object defaultValue = field.Name switch
            {
                "defaultTarget" => 255,
                "numberOfActivePlayers" => 1,
                _ => Activator.CreateInstance(field.FieldType)!,
            };

            bool eqGlobal = Equals(globalValue, localValue);
            bool eqDefault = Equals(defaultValue, localValue);
            if (eqGlobal && eqDefault)
                continue;

            MethodInfo toStringMethod = Utils.GetMethodOrThrow(field.FieldType, "ToString", []);
            if (toStringMethod.ReturnType != typeof(string))
                continue;

            StringBuilder builder = eqGlobal ? globalValues : mouseOverText;
            if (!eqGlobal && firstLocal)
            {
                mouseOverText.Append("Local parameters:\n");
                firstLocal = false;
            }

            string str = (string)toStringMethod.Invoke(localValue, [])!;
            builder.Append("  ");
            builder.Append(field.Name);
            builder.Append(": ");
            builder.Append(str);
            builder.Append('\n');
        }
        if (globalValues.Length > 0)
        {
            mouseOverText.Append("Global parameters:\n");
            mouseOverText.Append(globalValues);
        }
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