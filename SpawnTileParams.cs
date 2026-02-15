using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Terraria;

namespace SpawnAnalyzer;

public struct SpawnParamsStage1
{
    public bool skyMob;
    public bool xRange;


    public static bool operator ==(SpawnParamsStage1 a, SpawnParamsStage1 b)
    {
        return (a.skyMob == b.skyMob) && (a.xRange == b.xRange);
    }

    public static bool operator !=(SpawnParamsStage1 a, SpawnParamsStage1 b)
    {
        return !(a == b);
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is SpawnParamsStage1 stp && stp == this;
    }

    public override readonly int GetHashCode()
    {
        return (skyMob, xRange).GetHashCode();
    }

    public override readonly string ToString()
    {
        return $"{{ skyMob: {skyMob}, xRange: {xRange} }}";
    }
}

public struct SpawnParamsStage2
{
    public float nearMarbleChance;
    public float nearGraniteChance;

    public float spawnSpiderChance;
    public float spawnUndergroundDesertChance;
    public float isBeachChance;
    public float isOceanChance;
    public float surfaceSpawnChance;
    public float dayTimeChance;

    readonly static Func<NPC.Spawner, SpawnParamsStage2> InitFromSpawnerImpl = GenerateInitFromSpawnerMethod();

    public static SpawnParamsStage2 WithValuesFrom(NPC.Spawner spawner)
    {
        return InitFromSpawnerImpl(spawner);
    }

    static Func<NPC.Spawner, SpawnParamsStage2> GenerateInitFromSpawnerMethod()
    {
        DynamicMethodDefinition dmd = new("CopyChanceValuesFromSpawner", typeof(SpawnParamsStage2), [typeof(NPC.Spawner)]);

        ILProcessor il = dmd.GetILProcessor();
        VariableDefinition structVar = new(il.Import(typeof(SpawnParamsStage2)));
        il.Body.Variables.Add(structVar);

        il.Emit(OpCodes.Ldloca, structVar);
        il.Emit(OpCodes.Initobj, il.Import(typeof(SpawnParamsStage2)));

        foreach (FieldInfo field in typeof(SpawnParamsStage2).GetFields())
        {
            if (field.IsStatic || !field.Name.EndsWith("Chance") || field.FieldType != typeof(float))
                continue;

            string spawnerFieldName = field.Name.Substring(0, field.Name.Length - 6);
            FieldInfo? spawnerField = typeof(NPC.Spawner).GetField(spawnerFieldName, (BindingFlags)(-1));
            if (spawnerField is null || spawnerField.IsStatic || spawnerField.FieldType != typeof(bool))
                continue;

            Convert.ToSingle(true);

            il.Emit(OpCodes.Ldloca, structVar);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, il.Import(spawnerField));
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stfld, il.Import(field));
        }

        il.Emit(OpCodes.Ldloc, structVar);
        il.Emit(OpCodes.Ret);

        MethodInfo method = dmd.Generate();
        return method.CreateDelegate<Func<NPC.Spawner, SpawnParamsStage2>>();
    }
}