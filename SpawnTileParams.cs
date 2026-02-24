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

public class SpawnerChances
{
    [GlobalChanceField]
    public float spawnFriendlyChance;

    [GlobalChanceField]
    public float noWormsChance;

    public float nearMarbleChance;
    public float nearGraniteChance;

    public float spawnSpiderChance;
    public float spawnUndergroundDesertChance;
    public float isBeachChance;
    public float isOceanChance;
    public float surfaceSpawnChance;
    public float dayTimeChance;

    readonly static Func<NPC.Spawner, SpawnerChances> InitFromSpawnerImpl = GenerateInitFromSpawnerMethod();
    readonly static Action<SpawnerChances, SpawnerChances> CopyGlobalFieldsImpl = GenerateCopyGlobalFieldsMethod();

    public static SpawnerChances WithValuesFrom(NPC.Spawner spawner)
    {
        return InitFromSpawnerImpl(spawner);
    }

    public static void CopyGlobalFields(SpawnerChances from, SpawnerChances to)
    {
        CopyGlobalFieldsImpl(from, to);
    }

    static Func<NPC.Spawner, SpawnerChances> GenerateInitFromSpawnerMethod()
    {
        DynamicMethodDefinition dmd = new("CopyChanceValuesFromSpawner", typeof(SpawnerChances), [typeof(NPC.Spawner)]);

        ILProcessor il = dmd.GetILProcessor();
        VariableDefinition resultVar = new(il.Import(typeof(SpawnerChances)));
        il.Body.Variables.Add(resultVar);

        il.Emit(OpCodes.Newobj, il.Import(typeof(SpawnerChances).GetConstructor([]) ?? throw new MissingMethodException("SpawnerChances ctor")));
        il.Emit(OpCodes.Stloc, resultVar);

        foreach (FieldInfo field in typeof(SpawnerChances).GetFields())
        {
            if (field.IsStatic || !field.Name.EndsWith("Chance") || field.FieldType != typeof(float))
                continue;

            string spawnerFieldName = field.Name.Substring(0, field.Name.Length - 6);
            FieldInfo? spawnerField = typeof(NPC.Spawner).GetField(spawnerFieldName, (BindingFlags)(-1));
            if (spawnerField is null || spawnerField.IsStatic || spawnerField.FieldType != typeof(bool))
                continue;

            il.Emit(OpCodes.Ldloc, resultVar);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, il.Import(spawnerField));
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stfld, il.Import(field));
        }

        il.Emit(OpCodes.Ldloc, resultVar);
        il.Emit(OpCodes.Ret);

        MethodInfo method = dmd.Generate();
        return method.CreateDelegate<Func<NPC.Spawner, SpawnerChances>>();
    }

    static Action<SpawnerChances, SpawnerChances> GenerateCopyGlobalFieldsMethod()
    {
        DynamicMethodDefinition dmd = new("CopyGlobalChanceFields", typeof(void), [typeof(SpawnerChances), typeof(SpawnerChances)]);

        ILProcessor il = dmd.GetILProcessor();

        foreach (FieldInfo field in typeof(SpawnerChances).GetFields())
        {
            if (field.IsStatic || field.GetCustomAttribute<GlobalChanceFieldAttribute>() is null)
                continue;

            il.Emit(OpCodes.Ldarg_1); 
            il.Emit(OpCodes.Ldarg_0); 
            il.Emit(OpCodes.Ldfld, il.Import(field));
            il.Emit(OpCodes.Stfld, il.Import(field));
        }

        il.Emit(OpCodes.Ret);

        MethodInfo method = dmd.Generate();
        return method.CreateDelegate<Action<SpawnerChances, SpawnerChances>>();
    }
}

[AttributeUsage(AttributeTargets.Field)]
class GlobalChanceFieldAttribute : Attribute {}