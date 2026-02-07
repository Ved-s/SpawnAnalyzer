using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace SpawnAnalyzer;

static class DMDHack
{
    public static SetNullOriginalMethodDelegate SetNullOriginalMethod = GenerateSetNullOriginalMethod();
    public delegate void SetNullOriginalMethodDelegate(DynamicMethodDefinition dmd);

    static SetNullOriginalMethodDelegate GenerateSetNullOriginalMethod()
    {
        DynamicMethodDefinition d = new(typeof(DynamicMethodDefinition).GetProperty("OriginalMethod", (BindingFlags)(-1)).GetGetMethod());

        ILContext dil = new(d.Definition);

        FieldReference backingField = null!;

        if (!SpawnAnalyzer.MatchInstructions(
            dil, 0, out _,
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld(out backingField!)
        ))
        {
            Console.WriteLine("DMDHack fail");
            Environment.Exit(1);
        }

        DynamicMethodDefinition m = new("DMDHack_SetNullOriginalMethod", null, [typeof(DynamicMethodDefinition)]);
        ILProcessor il = m.GetILProcessor();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, backingField);
        il.Emit(OpCodes.Ret);

        MethodInfo info = m.Generate();
        return info.CreateDelegate<SetNullOriginalMethodDelegate>();
    }
}