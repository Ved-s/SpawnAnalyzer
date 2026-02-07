using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;

namespace SpawnAnalyzer;

class SpawnAnalysisSpot
{
    public Point Position;

    readonly int SpawnTileType;
    readonly int SpawnWallType;

    readonly bool xRange;

    readonly SpawnParamsStage2 Params2;

    readonly NPC.Spawner GlobalSpawner;
    readonly NPC.Spawner LocalSpawner;

    public SpawnAnalysisSpot(NPC.Spawner globalSpawner, Point position, SpawnParamsStage1 p)
    {
        GlobalSpawner = globalSpawner;
        Position = position;

        NPC.Spawner.GetProperGroundSpawnTileTypeAndWallType(position.X, position.Y, out SpawnTileType, out SpawnWallType);
        var spawner = (NPC.Spawner)FormatterServices.GetSafeUninitializedObject(typeof(NPC.Spawner));
        ShallowCloneFields(globalSpawner, spawner);
        LocalSpawner = spawner;

        LocalSpawner.skyMob = p.skyMob;
        xRange = p.xRange;

        SpawnParamsStage2 p2 = SpawnParamsStage2.WithValuesFrom(LocalSpawner);

        SpawnAnalyzer.SetSpawnFlagsForChosenTileImpl(LocalSpawner, Position.X, Position.Y, SpawnTileType, SpawnWallType, ref p2);

        Params2 = p2;
    }

    internal void MouseOver(StringBuilder mouseOverText)
    {
        mouseOverText.Append($"X: {Position.X} Y: {Position.Y}\n");
        mouseOverText.Append($"TileID: {SpawnTileType} WallID: {SpawnWallType}\n");

        if (xRange)
        {
            mouseOverText.Append($"  xRange: True\n");
        }
        if (LocalSpawner.skyMob && GlobalSpawner.skyMob)
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
            FieldInfo? chanceField = typeof(SpawnParamsStage2).GetField(chanceName, (BindingFlags)(-1));
            if (chanceField is not null)
            {
                float chance = (float)chanceField.GetValue(Params2);
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

            object globalValue = field.GetValue(GlobalSpawner);
            object localValue = field.GetValue(LocalSpawner);
            object defaultValue = field.Name switch
            {
                "defaultTarget" => 255,
                "numberOfActivePlayers" => 1,
                _ => Activator.CreateInstance(field.FieldType),
            };

            bool eqGlobal = Equals(globalValue, localValue);
            bool eqDefault = Equals(defaultValue, localValue);
            if (eqGlobal && eqDefault)
                continue;

            MethodInfo toStringMethod = field.FieldType.GetMethod("ToString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [], null);
            if (toStringMethod.ReturnType != typeof(string))
                continue;

            StringBuilder builder = eqGlobal ? globalValues : mouseOverText;
            if (!eqGlobal && firstLocal)
            {
                mouseOverText.Append("Local parameters:\n");
                firstLocal = false;
            }

            string str = (string)toStringMethod.Invoke(localValue, []);
            builder.Append("  ");
            builder.Append(field.Name);
            builder.Append(": ");
            builder.Append(str);
            builder.Append("\n");
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
            skyMob = LocalSpawner.skyMob,
            xRange = xRange,
        };
    }

}