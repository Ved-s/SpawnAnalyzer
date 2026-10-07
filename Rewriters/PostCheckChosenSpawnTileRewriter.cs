using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer;
using Terraria;
using Terraria.Utilities;
using Utils = SpawnAnalyzer.Utils;

public static class PostCheckChosenSpawnTileRewriter
{
    public static SimulatorImpl.PostCheckChosenSpawnTile GenerateMethod()
    {
        
        DynamicMethodDefinition dmd = new(Utils.GetMethodOrThrow<NPC.Spawner>("PostCheckChosenSpawnTile",
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
            ]
        ));

        dmd.Definition.IsStatic = false;
        dmd.Definition.HasThis = false;

        ILContext il = new(dmd.Definition);
        il.Invoke(RewriteMethod);

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo method = dmd.Generate();

        return method.CreateDelegate<SimulatorImpl.PostCheckChosenSpawnTile>();
    }

    static void RewriteMethod(ILContext il) {
        
        ILCursor c = new(il);

        int retval = 0;

        while (c.TryGotoNext(
            x=>x.MatchLdcI4(out retval),
            x=>x.MatchRet()
        )) {
            c.Next!.OpCode = OpCodes.Ldc_R4;
            c.Next.Operand = retval == 0 ? 0.0f : 1.0f;
        }

        /*
            -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            -ldc.i4.s  100
            -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            -ldc.i4.s  10
            -bge.s     IL_00F1
            +ldc.r4    0.9
            +ret
        */

        c.Index = 0;

        int max = 0;
        int threshold = 0;

        if (!c.TryGotoNext(
            MoveType.AfterLabel,
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out max),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchLdcI4(out threshold),
            x=>x.MatchBge(out _)
        )) {
            throw new Exception("PostCheckChosenSpawnTile patch fail");
        }

        float chance = 1.0f - ((float)threshold / (float)max);

        c.RemoveRange(5);
        c.Emit(OpCodes.Ldc_R4, chance);
        c.Emit(OpCodes.Ret);

        il.Method.ReturnType = il.Import(typeof(float));
    }
}