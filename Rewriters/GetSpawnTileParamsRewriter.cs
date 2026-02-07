using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.Utilities;

namespace SpawnAnalyzer.Rewriters;

static class GetSpawnTileParamsRewriter
{
    public static SpawnAnalyzer.GetSpawnTileParams GenerateMethod()
    {
        DynamicMethodDefinition dmd = new(typeof(NPC.Spawner).GetMethod("FindSpawnTile",
            BindingFlags.Instance | BindingFlags.Public, null,
            [
                typeof(Player),
                typeof(int).MakeByRefType(),
                typeof(int).MakeByRefType(),
                typeof(bool).MakeByRefType(),
            ],
            null
        ));

        dmd.Definition.IsStatic = false;
        dmd.Definition.HasThis = false;

        ILContext il = new(dmd.Definition);
        il.Invoke(RewriteMethod);

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo method = dmd.Generate();

        return method.CreateDelegate<SpawnAnalyzer.GetSpawnTileParams>();
    }

    static void RewriteMethod(ILContext il)
    {
        TypeReference intRefType = il.Import(typeof(int).MakeByRefType());
        TypeReference rectType = il.Import(typeof(Rectangle));
        TypeReference stpRefType = il.Import(typeof(SpawnParamsStage1).MakeByRefType());

        ParameterDefinition xRefParam = new("x", Mono.Cecil.ParameterAttributes.None, intRefType);
        ParameterDefinition yRefParam = new("y", Mono.Cecil.ParameterAttributes.None, intRefType);
        ParameterDefinition spawnRectParam = new("spawnRect", Mono.Cecil.ParameterAttributes.None, rectType);
        ParameterDefinition safeRectParam = new("safeRect", Mono.Cecil.ParameterAttributes.None, rectType);
        ParameterDefinition spawnParamsParam = new("spawnParams", Mono.Cecil.ParameterAttributes.None, stpRefType);

        ILLabel loopEnd = null!;
        int spawnRect = 0;
        int safeRect = 0;
        int posX = 0;
        int posY = 0;

        /*
            IL_0000: ldarg.1
            IL_0001: ldloca.s  spawnRect
            IL_0003: ldloca.s  safeRect
            IL_0005: call      void Terraria.NPC/Spawner::GetSpawnArea(class Terraria.Player, valuetype [FNA]Microsoft.Xna.Framework.Rectangle&, valuetype [FNA]Microsoft.Xna.Framework.Rectangle&)
            IL_000A: ldc.i4.0
            IL_000B: stloc.2
            IL_000C: br        loopEnd

            IL_0011: ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0016: ldloca.s  _
            IL_0018: call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Left()
            IL_001D: ldloca.s  _
            IL_001F: call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Right()
            IL_0024: callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            IL_0029: stloc     posX

            IL_002A: ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_002F: ldloca.s  _
            IL_0031: call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Top()
            IL_0036: ldloca.s  _
            IL_0038: call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Bottom()
            IL_003D: callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            IL_0042: stloc.s   posY
        */

        var beginningMatchers = new Func<Instruction, bool>[]
        {
            x=>x.MatchLdarg(1),
            x=>x.MatchLdloca(out spawnRect),
            x=>x.MatchLdloca(out safeRect),
            x=>x.MatchCall<NPC.Spawner>("GetSpawnArea"),
            x=>x.MatchLdcI4(0),
            x=>x.MatchStloc(out _),
            x=>x.MatchBr(out loopEnd!),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdloca(out _),
            x=>x.MatchCall<Rectangle>("get_Left"),
            x=>x.MatchLdloca(out _),
            x=>x.MatchCall<Rectangle>("get_Right"),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out posX),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdloca(out _),
            x=>x.MatchCall<Rectangle>("get_Top"),
            x=>x.MatchLdloca(out _),
            x=>x.MatchCall<Rectangle>("get_Bottom"),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out posY),
        };

        bool match = SpawnAnalyzer.MatchInstructions(il, 0, out int matchEndPos, beginningMatchers);
        if (!match)
        {
            Console.WriteLine($"GenerateGetSpawnTileParamsMethod match 1 fail at pos {matchEndPos}");
            if (matchEndPos >= il.Instrs.Count)
                Console.WriteLine($"Instruction OOB");
            else
                Console.WriteLine($"Instruction: {il.Instrs[matchEndPos]}");
            Environment.Exit(1);
            return;
        }

        ILCursor c = new(il);
        c.GotoLabel(loopEnd);

        c.Index -= 5;

        /*
            IL_01CA: ret

            IL_01CB: ldloc.2   // jump target, must be preserved
            IL_01CC: ldc.i4.1
            IL_01CD: add
            IL_01CE: stloc.2

            IL_01CF: ldloc.2
            IL_01D0: ldc.i4.s  50
            IL_01D2: blt       IL_0011

            IL_01D7: ldarg.2
            IL_01D8: ldc.i4.0
            IL_01D9: stind.i4
            IL_01DA: ldarg.3
            IL_01DB: ldc.i4.0
            IL_01DC: stind.i4
            IL_01DD: ldarg.s   4
            IL_01DF: ldc.i4.0
            IL_01E0: stind.i1
            IL_01E1: ldc.i4.0
            IL_01E2: ret
        */

        var endingMatchers = new Func<Instruction, bool>[]
        {
            x=>x.MatchRet(),
            x=>x.MatchLdloc(out _),
            x=>x.MatchLdcI4(1),
            x=>x.MatchAdd(),
            x=>x.MatchStloc(out _),

            x=>x.MatchLdloc(out _),
            x=>x.MatchLdcI4(50),
            x=>x.MatchBlt(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdcI4(0),
            x=>x.MatchStindI4(),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdcI4(0),
            x=>x.MatchStindI4(),
            x=>x.MatchLdarg(4),
            x=>x.MatchLdcI4(0),
            x=>x.MatchStindI1(),
            x=>x.MatchLdcI4(0),
            x=>x.MatchRet(),
        };

        match = SpawnAnalyzer.MatchInstructions(il, c.Index, out matchEndPos, endingMatchers);
        if (!match)
        {
            Console.WriteLine($"GenerateGetSpawnTileParamsMethod match 2 fail at pos {matchEndPos}");
            if (matchEndPos >= il.Instrs.Count)
                Console.WriteLine($"Instruction OOB");
            else
                Console.WriteLine($"Instruction: {il.Instrs[matchEndPos]}");
            Environment.Exit(1);
            return;
        }

        c.Index += 1;

        /*
            IL_01CA:  ret       
            IL_01CB: -ldloc.2   <-- Index, preserve
                     +ldc.i4.0
                     +ret
        */

        c.Next!.OpCode = OpCodes.Ldc_I4_0;
        c.Next.Operand = null;
        c.Index += 1;
        c.Emit(OpCodes.Ret);

        int remove = endingMatchers.Length - 2;
        int removeIndex = c.Index;
        c.Index = 0; // MonoMod will explode if it ever points to the end of instruction list
        for (int i = 0; i < remove; i++)
        {
            c.Instrs.RemoveAt(removeIndex);
        }

        c.Index = 0;
        c.RemoveRange(beginningMatchers.Length);

        // Assign parameters to required locals
        c.Emit(OpCodes.Ldarg, spawnRectParam);
        c.Emit(OpCodes.Stloc, spawnRect);
        c.Emit(OpCodes.Ldarg, safeRectParam);
        c.Emit(OpCodes.Stloc, safeRect);
        c.Emit(OpCodes.Ldarg, xRefParam);
        c.Emit(OpCodes.Ldind_I4);
        c.Emit(OpCodes.Stloc, posX);
        c.Emit(OpCodes.Ldarg, yRefParam);
        c.Emit(OpCodes.Ldind_I4);
        c.Emit(OpCodes.Stloc, posY);

        // Init tileParams
        c.Emit(OpCodes.Ldarg, spawnParamsParam);
        c.Emit(OpCodes.Initobj, typeof(SpawnParamsStage1));

        // replace all this.skyMob = true with tileParams.skyMob = true

        /*
            IL_00ED:  ldarg.0   <- jump target, preserve
		    IL_00EE:  ldc.i4.1
		    IL_00EF:  stfld     bool Terraria.NPC/Spawner::skyMob
                     +ldarg     4
                     +ldc.i4.1
                     +stfld     bool SpawnTileParams::skyMob
        */
        while (c.TryGotoNext(
            x => x.MatchLdarg(0),
            x => x.MatchLdcI4(1),
            x => x.MatchStfld<NPC.Spawner>("skyMob")
        ))
        {
            c.Next.OpCode = OpCodes.Ldarg;
            c.Next.Operand = spawnParamsParam;
            c.Index += 1;
            c.RemoveRange(2);
            c.Emit(OpCodes.Ldc_I4_1);
            c.Emit<SpawnParamsStage1>(OpCodes.Stfld, "skyMob");
        }

        // Make sure nothing sets fields in spawner
        c.Index = 0;

        string spawnerTypeFullName = il.Import(typeof(NPC.Spawner)).FullName;
        while (c.TryGotoNext(x => x.MatchStfld(out FieldReference? field) && field.DeclaringType.FullName == spawnerTypeFullName))
        {
            Console.WriteLine($"Spawner field setter! {il.Instrs[c.Index]}");
            Environment.Exit(1);
        }

        // Random spawn params are TODO, for now make sure there's no random
        c.Index = 0;

        // Make sure Next(_) == 0 never trigger
        /*
            IL_012B: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
		    IL_0130: -brtrue.s  target
                     +pop
                     +pop
                     +br        target
        */

        ILLabel target = null!;

        while (c.TryGotoNext(
            x => x.MatchCallvirt<UnifiedRandom>("Next"),
            x => x.MatchBrtrue(out target!)
        ))
        {
            c.Next!.OpCode = OpCodes.Pop;
            c.Next.Operand = null;
            c.Index += 1;
            c.RemoveRange(1);
            c.Emit(OpCodes.Pop);
            c.Emit(OpCodes.Br, target);
        }

        // Make sure no random calls exist now
        c.Index = 0;

        string unifiedRandomFullName = il.Import(typeof(UnifiedRandom)).FullName;
        while (c.TryGotoNext(x => x.MatchCallOrCallvirt(out MethodReference? method) && method.DeclaringType.FullName == unifiedRandomFullName))
        {
            Console.WriteLine($"Random still referenced! {il.Instrs[c.Index]}");
            Environment.Exit(1);
        }

        // Retarget old out arg assignments to new args
        /*
            IL_018E: ldarg.2
		    IL_018F: ldloc.3
		    IL_0190: stind.i4
		    IL_0191: ldarg.3
		    IL_0192: ldloc.s   4
		    IL_0194: stind.i4
        */

        var oldOutAssignmentMatchers = new Func<Instruction, bool>[]
        {
            x=>x.MatchLdarg(2),
            x=>x.MatchLdloc(out _),
            x=>x.MatchStindI4(),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdloc(out _),
            x=>x.MatchStindI4(),
        };

        c.Index = 0;
        if (!c.TryGotoNext(oldOutAssignmentMatchers))
        {
            Console.WriteLine("Could not match old out assignments");
            Environment.Exit(1);
        }

        c.Next.Operand = xRefParam;
        c.Index += 3;
        c.Next.Operand = yRefParam;

        // // Replace old coord arg accessors with new ones

        // /*
        //     IL_0199:  ldarg     2 or 3
        //     IL_019A: -ldind.i4
        // */

        // c.Index = 0;
        // int arg = 0;
        // while (c.TryGotoNext(
        //     x => x.MatchLdarg(out arg) && (arg == 2 || arg == 3),
        //     x => x.MatchLdindI4()
        // ))
        // {
        //     c.Next.Operand = arg == 2 ? xParam : yParam;
        //     c.Index += 1;
        //     c.Remove();
        // }

        // Replace xRange set

        /*
            IL_01AD:  ldarg.s   4
		    IL_01AF:  ldarg.2
            IL_01B0:  ldind.i4
		    IL_01B1:  ldloca.s  V_1
		    IL_01B3:  call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Left()
		    IL_01B8:  blt.s     IL_01C7
 
		    IL_01BA:  ldarg.2
            IL_01BB:  ldind.i4
		    IL_01BC:  ldloca.s  V_1
		    IL_01BE:  call      instance int32 [FNA]Microsoft.Xna.Framework.Rectangle::get_Right()
		    IL_01C3:  clt
		    IL_01C5:  br.s      IL_01C8
 
		    IL_01C7:  ldc.i4.0

		    IL_01C8: -stind.i1
                     +stfld     SpawnTileParams::xRange
        */

        c.Index = 0;
        if (!c.TryGotoNext(
            x => x.MatchLdarg(4),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),

            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),
            x => !x.MatchStindI1(),

            x => !x.MatchStindI1(),

            x => x.MatchStindI1()
        ))
        {
            Console.WriteLine("xRange replace match fail");
            Environment.Exit(1);
        }

        c.Next.Operand = spawnParamsParam;
        c.Index += 13;
        Debug.Assert(c.Next.OpCode == OpCodes.Stind_I1);
        c.Next.OpCode = OpCodes.Stfld;
        c.Next.Operand = typeof(SpawnParamsStage1).GetField("xRange", (BindingFlags)(-1))!;

        //             [0]                  [1]            [2]                 [3]                 [4]                  [5]                [6]
        // old params: NPC.Spawner self,    Player player, out int spawnTileX, out int spawnTileY, out bool xRange
        // new params: NPC.Spawner spawner, Player player, int x,              int y,              Rectangle spawnArea, Rectange safeArea, out SpawnTileParams tileParams

        il.Method.IsStatic = true;
        il.Method.HasThis = false;

        il.Method.Parameters.RemoveAt(2);
        il.Method.Parameters.RemoveAt(2);
        il.Method.Parameters.RemoveAt(2);

        il.Method.Parameters.Add(xRefParam);
        il.Method.Parameters.Add(yRefParam);
        il.Method.Parameters.Add(spawnRectParam);
        il.Method.Parameters.Add(safeRectParam);
        il.Method.Parameters.Add(spawnParamsParam);

        foreach (ILLabel label in il.Labels)
        {
            List<Instruction> branches = label.Branches.ToList();
            if (branches.Count == 0)
            {
                label.Target = il.Instrs[0];
            }
            else if (il.IndexOf(label.Target) >= il.Instrs.Count)
            {
                Console.WriteLine($"Instructions: {il.Instrs.Count}");
                Console.WriteLine($"Label has {branches.Count} jumps but no target:");
                foreach (Instruction t in branches)
                {
                    Console.WriteLine($"[{il.IndexOf(t)}] Offset {t.Offset:x}");
                }
                Console.Out.Flush();
                Environment.Exit(1);
            }
        }
    }
}