using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SpawnAnalyzer;

public static class ReflectionHelpers
{
    public static Action<Inst, Val> GenerateInstanceFieldSetter<Inst, Val>(FieldInfo field)
    {
        DynamicMethod dm = new($"FieldSetter<{field.DeclaringType?.FullName}.{field.Name}>", typeof(void), [typeof(Inst), typeof(Val)], true);

        ILGenerator il = dm.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        return (Action<Inst, Val>)dm.CreateDelegate(typeof(Action<Inst, Val>));
    }

    public static Func<Inst, Val> GenerateInstanceFieldGetter<Inst, Val>(FieldInfo field)
    {
        DynamicMethod dm = new($"FieldGetter<{field.DeclaringType?.FullName}.{field.Name}>", typeof(Val), [typeof(Inst)], true);

        ILGenerator il = dm.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);

        return (Func<Inst, Val>)dm.CreateDelegate(typeof(Func<Inst, Val>));
    }
}