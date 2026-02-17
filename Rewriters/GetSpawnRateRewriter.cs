using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.Utilities;

namespace SpawnAnalyzer.Rewriters;

public class GetSpawnRateRewriter
{
    internal static SpawnAnalyzer.GetSpawnRate GenerateMethod()
    {
        DynamicMethodDefinition dmd = new(Utils.GetMethodOrThrow<NPC.Spawner>("GetSpawnRate",
            [
                typeof(Player),
                typeof(int).MakeByRefType(),
                typeof(int).MakeByRefType(),
            ]
        ));

        dmd.Definition.IsStatic = false;
        dmd.Definition.HasThis = false;

        ILContext il = new(dmd.Definition);
        il.Invoke(RewriteMethod);

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo method = dmd.Generate();
        return method.CreateDelegate<SpawnAnalyzer.GetSpawnRate>();
    }

    static void RewriteMethod(ILContext il)
    {
        ParameterDefinition chancesParam = new(il.Import(typeof(SpawnerChances)));

        ILCursor c = new(il);

        RewritePattern1(c, chancesParam);
        RewritePattern2(c, chancesParam);
        RewritePattern3(c, chancesParam);
        RewritePattern4(c, chancesParam);

        ReplaceOldFieldSetters(c, chancesParam);

        c.Index = 0;
        if (c.TryGotoNext(
            x => x.MatchLdfld<Main>("rand") 
              || (x.MatchCallOrCallvirt(out MethodReference? mr) && mr.DeclaringType.Is(typeof(NPC.Spawner)))
        ))
        {
            throw new InvalidOperationException($"Found unwanted instruction at IL_{c.Next!.Offset}");
        }

        il.Method.Parameters.Add(chancesParam);
    }

    static void RewritePattern1(ILCursor c, ParameterDefinition chancesParam)
    {
        c.Index = 0;
        /*
            if (Main.rand.Next($rand1chance) $rand1branchinstr 0)
			{
				this.noWorms = true;
			}
			if (Main.rand.Next($rand2chance) $rand2branchinstr 0)
			{
				this.spawnFriendly = true;
				maxSpawns = (int)((double)((float)maxSpawns) * $maxSpawnsMult);
			}
			else
			{
				spawnRate = (int)((double)((float)spawnRate) * $spawnRateMult);
			}

            IL_0B57: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0B5C: -ldc.i4    rand1chance
            IL_0B5D: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0B62: -brtrue.s  IL_0B6B  OR brfalse @ rand1branchinstr

            IL_0B64: -ldarg.0
            IL_0B65: -ldc.i4.1
            IL_0B66: -stfld     bool Terraria.NPC/Spawner::noWorms

            IL_0B6B: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0B70: -ldc.i4.s  rand2chance
            IL_0B72: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0B77: -brtrue.s  IL_0B96  OR brfalse @ rand2branchinstr

            IL_0B79: -ldarg.0
            IL_0B7A: -ldc.i4.1
            IL_0B7B: -stfld     bool Terraria.NPC/Spawner::spawnFriendly
            IL_0B80: -ldarg.3
            IL_0B81: -ldarg.3
            IL_0B82: -ldind.i4
            IL_0B83: -conv.r4
            IL_0B84: -conv.r8
            IL_0B85: -ldc.r8    maxSpawnsMult
            IL_0B8E: -mul
            IL_0B8F: -conv.i4
            IL_0B90: -stind.i4
            IL_0B91: -br        IL_0E56

            IL_0B96: -ldarg.2
            IL_0B97: -ldarg.2
            IL_0B98: -ldind.i4
            IL_0B99: -conv.r4
            IL_0B9A: -conv.r8
            IL_0B9B: -ldc.r8    spawnRateMult
            IL_0BA4: -mul
            IL_0BA5: -conv.i4
            IL_0BA6: -stind.i4

                     +ldc.i4    rand1chance
                     +ldc.i4    rand2chance
                     +ldc.i4    rand1branchtype
                     +ldc.i4    rand2branchtype
                     +ldc.r4    maxSpawnsMult
                     +ldc.r4    spawnRateMult
                     +ldarg     2
                     +ldarg     3
                     +ldarg     chancesParam
                     +call      HandlePattern1

            IL_0BA7:  br        IL_0E56
        */

        int rand1chance = 0;
        int rand2chance = 0;

        Instruction rand1branchinstr = null!;
        Instruction rand2branchinstr = null!;

        double maxSpawnsMult = 0;
        double spawnRateMult1 = 0;
        float spawnRateMult2 = 0;

        Func<Instruction, bool>[] matchersInit = [
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out rand1chance),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>{rand1branchinstr = x; return x.MatchBrfalse(out _) || x.MatchBrtrue(out _);},

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("noWorms"),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out rand2chance),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>{rand2branchinstr = x; return x.MatchBrfalse(out _) || x.MatchBrtrue(out _);},

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("spawnFriendly"),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
        ];

        Func<Instruction, bool>[] matchersEndV1 = [
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out spawnRateMult1),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),
        ];

        Func<Instruction, bool>[] matchersEndV2 = [
            x=>x.MatchLdcR4(out spawnRateMult2),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),
        ];

        while (c.TryGotoNext(
            MoveType.AfterLabel,
            matchersInit
        ))
        {
            float spawnRateMult;
            int removeEndLength;
            int endstart = c.Index + matchersInit.Length;
            if (SpawnAnalyzer.MatchInstructions(c.Context, endstart, out _, matchersEndV1))
            {
                removeEndLength = matchersEndV1.Length;
                spawnRateMult = (float)spawnRateMult1;
            }
            else if (SpawnAnalyzer.MatchInstructions(c.Context, endstart, out _, matchersEndV2))
            {
                removeEndLength = matchersEndV2.Length;
                spawnRateMult = (float)spawnRateMult2;
            }
            else
            {
                c.Goto(c.Next!.Next);
                continue;
            }

            c.RemoveRange(matchersInit.Length + removeEndLength - 1);

            c.Emit(OpCodes.Ldc_I4, rand1chance);
            c.Emit(OpCodes.Ldc_I4, rand2chance);
            c.Emit(OpCodes.Ldc_I4, Convert.ToInt32(rand1branchinstr.MatchBrtrue(out _)));
            c.Emit(OpCodes.Ldc_I4, Convert.ToInt32(rand2branchinstr.MatchBrtrue(out _)));
            c.Emit(OpCodes.Ldc_R4, (float)maxSpawnsMult);
            c.Emit(OpCodes.Ldc_R4, (float)spawnRateMult);
            c.Emit(OpCodes.Ldarg_2);
            c.Emit(OpCodes.Ldarg_3);
            c.Emit(OpCodes.Ldarg, chancesParam);
            c.Emit<GetSpawnRateRewriter>(OpCodes.Call, nameof(HandlePattern1));
        }
    }

    static void HandlePattern1(
        int rand1chance,
        int rand2chance,
        bool rand1branchtype,
        bool rand2branchtype,
        float maxSpawnsMult,
        float spawnRateMult,
        ref int spawnRate,
        ref int maxSpawns,
        SpawnerChances chances
    )
    {
        float rand1branchchance = 1f / rand1chance;
        if (!rand1branchtype)
            rand1branchchance = 1 - rand1branchchance;

        chances.noWormsChance = SpawnAnalyzer.CombineChances(chances.noWormsChance, rand1branchchance);

        float rand2branchtruechance = 1f / rand2chance;
        if (!rand2branchtype)
            rand2branchtruechance = 1 - rand2branchtruechance;

        float rand2branchfalsechance = 1 - rand2branchtruechance;

        chances.spawnFriendlyChance = SpawnAnalyzer.CombineChances(chances.spawnFriendlyChance, rand2branchtruechance);

        float maxSpawnsAvgMult = SpawnAnalyzer.PredictAverageRandomChanceMultiplier(rand2branchtruechance, maxSpawnsMult);
        float spawnRateAvgMult = SpawnAnalyzer.PredictAverageRandomChanceMultiplier(rand2branchfalsechance, spawnRateMult);

        spawnRate = (int)(spawnRate * spawnRateAvgMult);
        maxSpawns = (int)(maxSpawns * maxSpawnsAvgMult);
    }

    static void RewritePattern2(ILCursor c, ParameterDefinition chancesParam)
    {
        c.Index = 0;

        /*
            if (this.ZoneGraveyard && (!this.ZonePeaceCandle || Main.rand.Next(3) == 0))
			{
				spawnRate = (int)((double)((float)spawnRate) * $br1spawnRateMult);
				if (Main.rand.Next($br1randchance) == 1)
				{
					this.spawnFriendly = true;
					maxSpawns = (int)((double)((float)maxSpawns) * $br1maxSpawnsMult);
				}
			}
			else if (Main.rand.Next(3) $br2isbrfalse)
			{
				this.spawnFriendly = true;
				maxSpawns = (int)((double)((float)maxSpawns) * $br2maxSpawnsMult);
			}
			else
			{
				spawnRate = (int)((float)spawnRate * $br3spawnRateMult);
			}

            IL_0C82: -ldarg.0
            IL_0C83: -ldfld     bool Terraria.NPC/Spawner::ZoneGraveyard
            IL_0C88: -brfalse.s IL_0CDF

            IL_0C8A: -ldarg.0
            IL_0C8B: -ldfld     bool Terraria.NPC/Spawner::ZonePeaceCandle
            IL_0C90: -brfalse.s IL_0C9F

            IL_0C92: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0C97: -ldc.i4.3
            IL_0C98: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0C9D: -brtrue.s  IL_0CDF

            IL_0C9F: -ldarg.2
            IL_0CA0: -ldarg.2
            IL_0CA1: -ldind.i4
            IL_0CA2: -conv.r4
            IL_0CA3: -conv.r8
            IL_0CA4: -ldc.r8    $br1spawnRateMult
            IL_0CAD: -mul
            IL_0CAE: -conv.i4
            IL_0CAF: -stind.i4

            IL_0CB0: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0CB5: -ldc.i4.s  $br1randchance
            IL_0CB7: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0CBC: -ldc.i4.1
            IL_0CBD: -bne.un    IL_0E56

            IL_0CC2: -ldarg.0
            IL_0CC3: -ldc.i4.1
            IL_0CC4: -stfld     bool Terraria.NPC/Spawner::spawnFriendly

            IL_0CC9: -ldarg.3
            IL_0CCA: -ldarg.3
            IL_0CCB: -ldind.i4
            IL_0CCC: -conv.r4
            IL_0CCD: -conv.r8
            IL_0CCE: -ldc.r8    $br1maxSpawnsMult
            IL_0CD7: -mul
            IL_0CD8: -conv.i4
            IL_0CD9: -stind.i4
            IL_0CDA: -br        IL_0E56

            IL_0CDF: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0CE4: -ldc.i4.3
            IL_0CE5: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)

            -----
            IL_0CEA: -ldc.i4.1
            IL_0CEB: -bne.un.s  IL_0D0A

            OR 

            IL_0D95: -brfalse.s IL_0DB4

            ----- @ $br2isbrfalse

            IL_0CED: -ldarg.0
            IL_0CEE: -ldc.i4.1
            IL_0CEF: -stfld     bool Terraria.NPC/Spawner::spawnFriendly

            IL_0CF4: -ldarg.3
            IL_0CF5: -ldarg.3
            IL_0CF6: -ldind.i4
            IL_0CF7: -conv.r4
            IL_0CF8: -conv.r8
            IL_0CF9: -ldc.r8    $br2maxSpawnsMult
            IL_0D02: -mul
            IL_0D03: -conv.i4
            IL_0D04: -stind.i4
            IL_0D05: -br        IL_0E56

            IL_0D0A: -ldarg.2
            IL_0D0B: -ldarg.2
            IL_0D0C: -ldind.i4
            IL_0D0D: -conv.r4
            IL_0D0E: -ldc.r4    br3spawnRateMult
            IL_0D13: -mul
            IL_0D14: -conv.i4
            IL_0D15: -stind.i4
                     +ldarg.0
                     +ldc.i4    br1randchance
                     +ldc.r4    br1spawnRateMult
                     +ldc.r4    br1maxSpawnsMult
                     +ldc.r4    br2maxSpawnsMult
                     +ldc.r4    br3spawnRateMult
                     +ldc.14    br2isbrfalse
                     +ldarg.2
                     +ldarg.3
                     +ldarg     chancesParam
                     +call      HandlePattern2

            IL_0D16:  br        IL_0E56
        */

        int br1randchance = 0;
        double br1spawnRateMult = 0;
        double br1maxSpawnsMult = 0;
        double br2maxSpawnsMult = 0;
        float br3spawnRateMult = 0;

        Func<Instruction, bool>[] matchers1 = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("ZoneGraveyard"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("ZonePeaceCandle"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(3),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchBrtrue(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out br1spawnRateMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out br1randchance),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchLdcI4(1),
            x=>x.MatchBneUn(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("spawnFriendly"),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out br1maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(3),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
        ];

        Func<Instruction, bool>[] matchers2bne1 = [
            x=>x.MatchLdcI4(1),
            x=>x.MatchBneUn(out _),
        ];

        Func<Instruction, bool>[] matchers2brfalse = [
            x=>x.MatchBrfalse(out _),
        ];

        Func<Instruction, bool>[] matchers3 = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("spawnFriendly"),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out br2maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchLdcR4(out br3spawnRateMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),
        ];

        while (c.TryGotoNext(
            MoveType.AfterLabel,
            matchers1
        ))
        {
            int index = c.Index;
            int match2pos = index + matchers1.Length;

            bool br2isbrfalse;
            int match2len;

            if (SpawnAnalyzer.MatchInstructions(c.Context, match2pos, out _, matchers2bne1))
            {
                br2isbrfalse = false;
                match2len = matchers2bne1.Length;
            }
            else if (SpawnAnalyzer.MatchInstructions(c.Context, match2pos, out _, matchers2brfalse))
            {
                br2isbrfalse = true;
                match2len = matchers2brfalse.Length;
            }
            else
            {
                c.Goto(c.Next!.Next);
                continue;
            }

            int match3pos = match2pos + match2len;
            if (!SpawnAnalyzer.MatchInstructions(c.Context, match3pos, out _, matchers3))
            {
                c.Goto(c.Next!.Next);
                continue;
            }

            c.RemoveRange(matchers1.Length + match2len + matchers3.Length - 1);

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldc_I4, br1randchance);
            c.Emit(OpCodes.Ldc_R4, (float)br1spawnRateMult);
            c.Emit(OpCodes.Ldc_R4, (float)br1maxSpawnsMult);
            c.Emit(OpCodes.Ldc_R4, (float)br2maxSpawnsMult);
            c.Emit(OpCodes.Ldc_R4, br3spawnRateMult);
            c.Emit(OpCodes.Ldc_I4, Convert.ToInt32(br2isbrfalse));
            c.Emit(OpCodes.Ldarg_2);
            c.Emit(OpCodes.Ldarg_3);
            c.Emit(OpCodes.Ldarg, chancesParam);
            c.Emit<GetSpawnRateRewriter>(OpCodes.Call, nameof(HandlePattern2));
        }
    }

    static void HandlePattern2(
        NPC.Spawner spawner,
        int br1randchance,
        float br1spawnRateMult,
        float br1maxSpawnsMult,
        float br2maxSpawnsMult,
        float br3spawnRateMult,
        bool br2isbrfalse,
        ref int spawnRate,
        ref int maxSpawns,
        SpawnerChances chances
    )
    {
        float br1chance;

        if (!spawner.ZoneGraveyard)
            br1chance = 0;
        else if (!spawner.ZonePeaceCandle)
            br1chance = 1;
        else
            br1chance = 1f / 3;

        float br2chanceraw;

        if (br2isbrfalse)
            br2chanceraw = 2f / 3;
        else
            br2chanceraw = 1f / 3;

        float br2chance = (1 - br1chance) * br2chanceraw;

        float br3chance = 1 - (br1chance + br2chance);

        float br1achanceraw = 1f / br1randchance;
        float br1achance = br1chance * br1achanceraw;

        spawnRate = (int)(spawnRate * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br1chance, br1spawnRateMult));

        chances.spawnFriendlyChance = SpawnAnalyzer.CombineChances(chances.spawnFriendlyChance, br1achance);
        maxSpawns = (int)(maxSpawns * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br1achance, br1maxSpawnsMult));

        chances.spawnFriendlyChance = SpawnAnalyzer.CombineChances(chances.spawnFriendlyChance, br2chance);
        maxSpawns = (int)(maxSpawns * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br2chance, br2maxSpawnsMult));

        spawnRate = (int)(spawnRate * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br3chance, br3spawnRateMult));
    }

    static void RewritePattern3(ILCursor c, ParameterDefinition chancesParam)
    {
        c.Index = 0;

        /*
            this.noWorms = true;
			if (this.ZoneGraveyard && (!this.ZonePeaceCandle || Main.rand.Next(3) == 0))
			{
				spawnRate = (int)((float)spawnRate * $br1spawnRateMult);
				if (Main.rand.Next(3) == 1)
				{
					this.spawnFriendly = true;
					maxSpawns = (int)((double)((float)maxSpawns) * $br1maxSpawnsMult);
				}
			}
			else
			{
				if (!Main.expertMode || Main.rand.Next(30) != 0)
				{
					this.spawnFriendly = true;
				}
				maxSpawns = (int)((double)((float)maxSpawns) * $br2maxSpawnsMult);
			}

            IL_0DD8: -ldarg.0
            IL_0DD9: -ldfld     bool Terraria.NPC/Spawner::ZoneGraveyard
            IL_0DDE: -brfalse.s IL_0E29

            IL_0DE0: -ldarg.0
            IL_0DE1: -ldfld     bool Terraria.NPC/Spawner::ZonePeaceCandle
            IL_0DE6: -brfalse.s IL_0DF5

            IL_0DE8: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0DED: -ldc.i4.3
            IL_0DEE: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0DF3: -brtrue.s  IL_0E29

            IL_0DF5: -ldarg.2
            IL_0DF6: -ldarg.2
            IL_0DF7: -ldind.i4
            IL_0DF8: -conv.r4
            IL_0DF9: -ldc.r4    $br1spawnRateMult
            IL_0DFE: -mul
            IL_0DFF: -conv.i4
            IL_0E00: -stind.i4

            IL_0E01: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0E06: -ldc.i4.3
            IL_0E07: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0E0C: -ldc.i4.1
            IL_0E0D: -bne.un.s  IL_0E56

            IL_0E0F: -ldarg.0
            IL_0E10: -ldc.i4.1
            IL_0E11: -stfld     bool Terraria.NPC/Spawner::spawnFriendly

            IL_0E16: -ldarg.3
            IL_0E17: -ldarg.3
            IL_0E18: -ldind.i4
            IL_0E19: -conv.r4
            IL_0E1A: -conv.r8
            IL_0E1B: -ldc.r8    $br1maxSpawnsMult
            IL_0E24: -mul
            IL_0E25: -conv.i4
            IL_0E26: -stind.i4
            IL_0E27: -br.s      IL_0E56

            IL_0E29: -call      bool Terraria.Main::get_expertMode()
            IL_0E2E: -brfalse.s IL_0E3E

            IL_0E30: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            IL_0E35: -ldc.i4.s  30
            IL_0E37: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            IL_0E3C: -brfalse.s IL_0E45

            IL_0E3E: -ldarg.0
            IL_0E3F: -ldc.i4.1
            IL_0E40: -stfld     bool Terraria.NPC/Spawner::spawnFriendly

            IL_0E45: -ldarg.3
            IL_0E46: -ldarg.3
            IL_0E47: -ldind.i4
            IL_0E48: -conv.r4
            IL_0E49: -conv.r8
            IL_0E4A: -ldc.r8    $br2maxSpawnsMult
            IL_0E53: -mul
            IL_0E54: -conv.i4
            IL_0E55: -stind.i4

                     +ldarg.0
                     +ldc.r4    $br1spawnRateMult
                     +ldc.r4    $br1maxSpawnsMult
                     +ldc.r4    $br2maxSpawnsMult
                     +ldarg.2
                     +ldarg.3
                     +ldarg     chancesParam
                     +call      HandlePattern3
        */

        float br1spawnRateMult = 0;
        double br1maxSpawnsMult = 0;
        double br2maxSpawnsMult = 0;

        Func<Instruction, bool>[] matchers = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("ZoneGraveyard"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("ZonePeaceCandle"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(3),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchBrtrue(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchLdcR4(out br1spawnRateMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(3),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchLdcI4(1),
            x=>x.MatchBneUn(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("spawnFriendly"),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out br1maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
            x=>x.MatchBr(out _),

            x=>x.MatchCall<Main>("get_expertMode"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(30),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchBrfalse(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("spawnFriendly"),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchConvR8(),
            x=>x.MatchLdcR8(out br2maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4()  
        ];

        if (c.TryGotoNext(
            MoveType.AfterLabel,
            matchers
        ))
        {
            c.RemoveRange(matchers.Length);

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldc_R4, br1spawnRateMult);
            c.Emit(OpCodes.Ldc_R4, (float)br1maxSpawnsMult);
            c.Emit(OpCodes.Ldc_R4, (float)br2maxSpawnsMult);
            c.Emit(OpCodes.Ldarg_2);
            c.Emit(OpCodes.Ldarg_3);
            c.Emit(OpCodes.Ldarg, chancesParam);
            c.Emit<GetSpawnRateRewriter>(OpCodes.Call, nameof(HandlePattern3));
        }
    }

    static void HandlePattern3(
        NPC.Spawner spawner,
        float br1spawnRateMult,
        float br1maxSpawnsMult,
        float br2maxSpawnsMult,
        ref int spawnRate,
        ref int maxSpawns,
        SpawnerChances chances
    )
    {
        float br1chance;

        if (!spawner.ZoneGraveyard)
            br1chance = 0;
        else if (!spawner.ZonePeaceCandle)
            br1chance = 1;
        else
            br1chance = 1f / 3;

        float br2chance = 1 - br1chance;

        float br1achanceraw = 1f / 3;
        float br1achance = br1chance * br1achanceraw;

        float br2achanceraw;

        if (!Main.expertMode)
            br2achanceraw = 1;
        else 
            br2achanceraw = 29f/30;

        float br2achance = br2chance * br2achanceraw;

        spawnRate = (int)(spawnRate * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br1chance, br1spawnRateMult));

        chances.spawnFriendlyChance = SpawnAnalyzer.CombineChances(chances.spawnFriendlyChance, br1achance);
        maxSpawns = (int)(maxSpawns * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br1achance, br1maxSpawnsMult));

        chances.spawnFriendlyChance = SpawnAnalyzer.CombineChances(chances.spawnFriendlyChance, br2achance);

        maxSpawns = (int)(maxSpawns * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(br2chance, br2maxSpawnsMult));        
    }

    static void RewritePattern4(ILCursor c, ParameterDefinition chancesParam)
    {
        c.Index = 0;

        /*
            if (!this.spawnFriendly && this.RollOnlyBadLuckExtreme(50) == 0)
            {
                spawnRate = (int)((float)spawnRate * $spawnRateMult);
                maxSpawns = (int)((float)maxSpawns * $maxSpawnsMult);
            }

            IL_0E56: -ldarg.0
            IL_0E57: -ldfld     bool Terraria.NPC/Spawner::spawnFriendly
            IL_0E5C: -brtrue.s  IL_0E80

            IL_0E5E: -ldarg.0
            IL_0E5F: -ldc.i4.s  50
            IL_0E61: -call      instance int32 Terraria.NPC/Spawner::RollOnlyBadLuckExtreme(int32)
            IL_0E66: -brtrue.s  IL_0E80

            IL_0E68: -ldarg.2
            IL_0E69: -ldarg.2
            IL_0E6A: -ldind.i4
            IL_0E6B: -conv.r4
            IL_0E6C: -ldc.r4    $spawnRateMult
            IL_0E71: -mul
            IL_0E72: -conv.i4
            IL_0E73: -stind.i4

            IL_0E74: -ldarg.3
            IL_0E75: -ldarg.3
            IL_0E76: -ldind.i4
            IL_0E77: -conv.r4
            IL_0E78: -ldc.r4    $maxSpawnsMult
            IL_0E7D: -mul
            IL_0E7E: -conv.i4
            IL_0E7F: -stind.i4

                     +ldarg.0
                     +ldc.r4    $spawnRateMult
                     +ldc.r4    $maxSpawnsMult
                     +ldarg.2
                     +ldarg.3
                     +ldarg     chancesParam
                     +call      HandlePattern3
        */

        float spawnRateMult = 0;
        float maxSpawnsMult = 0;

        Func<Instruction, bool>[] matchers = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("spawnFriendly"),
            x=>x.MatchBrtrue(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(50),
            x=>x.MatchCall<NPC.Spawner>("RollOnlyBadLuckExtreme"),
            x=>x.MatchBrtrue(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchLdcR4(out spawnRateMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),

            x=>x.MatchLdarg(3),
            x=>x.MatchLdarg(3),
            x=>x.MatchLdindI4(),
            x=>x.MatchConvR4(),
            x=>x.MatchLdcR4(out maxSpawnsMult),
            x=>x.MatchMul(),
            x=>x.MatchConvI4(),
            x=>x.MatchStindI4(),
        ];

        if (c.TryGotoNext(
            MoveType.AfterLabel,
            matchers
        ))
        {
            c.RemoveRange(matchers.Length);

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldc_R4, spawnRateMult);
            c.Emit(OpCodes.Ldc_R4, maxSpawnsMult);
            c.Emit(OpCodes.Ldarg_2);
            c.Emit(OpCodes.Ldarg_3);
            c.Emit(OpCodes.Ldarg, chancesParam);
            c.Emit<GetSpawnRateRewriter>(OpCodes.Call, nameof(HandlePattern4));
        }
    }

    static void HandlePattern4(
        NPC.Spawner spawner,
        float spawnRateMult,
        float maxSpawnsMult,
        ref int spawnRate,
        ref int maxSpawns,
        SpawnerChances chances
    )
    {
        float notSpawnFriendly = 1 - chances.spawnFriendlyChance;
        float rollChance = 0;

        if (spawner.luck < 0) 
            rollChance = SpawnAnalyzer.PredictBadLuckExtremeChanceMod(spawner.luck);

        float branchChance = notSpawnFriendly * rollChance;

        spawnRate = (int)(spawnRate * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(branchChance, spawnRateMult));  
        maxSpawns = (int)(maxSpawns * SpawnAnalyzer.PredictAverageRandomChanceMultiplier(branchChance, maxSpawnsMult));  
    }

    static void ReplaceOldFieldSetters(ILCursor c, ParameterDefinition chancesParam)
    {
        c.Index = 0;
        VariableDefinition tempFloat = new(c.Context.Import(typeof(float)));
        c.Context.Body.Variables.Add(tempFloat);

        FieldReference replaceField = null!;
        while (c.TryGotoNext(
            x => x.MatchStfld(out replaceField!) && replaceField.DeclaringType.Is(typeof(NPC.Spawner))
        ))
        {
            if (typeof(SpawnerChances).GetField(replaceField.Name + "Chance", (BindingFlags)(-1)) is null)
                throw new InvalidOperationException($"GetSpawnRate sets spawner field {replaceField.Name} but no corresponding chance field was found");

            if (c.Prev.MatchLdcI4(out int fieldValue) && c.Instrs[c.Index - 2].MatchLdarg(0))
            {
                c.Index -= 2;

                /*
                    -ldarg     0
                    -ldc.i4    fieldValue
                    -stfld     $replaceField
                    +ldarg     chancesParam
                    +ldc.r4    fieldValue
                    +stfld     SpawnerChances::$replaceFieldChance
                */

                c.Next!.OpCode = OpCodes.Ldarg;
                c.Next.Operand = chancesParam;
                c.Index += 1;
                c.Next.OpCode = OpCodes.Ldc_R4;
                c.Next.Operand = (float)fieldValue;
                c.Index += 1;
                c.Next.Operand = new FieldReference(replaceField.Name + "Chance", c.Context.Import(typeof(float)), c.Context.Import(typeof(SpawnerChances)));
            }
            else
            {
                /*
                    -stfld     $replaceField
                    +conv.r4
                    +stloc     tempFloat
                    +pop
                    +ldarg     chancesParam
                    +ldloc     tempFloat
                    +stfld     SpawnerChances::$replaceFieldChance
                */
                c.Next!.OpCode = OpCodes.Conv_R4;
                c.Next.Operand = null;
                c.Index += 1;

                c.Emit(OpCodes.Stloc, tempFloat);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldarg, chancesParam);
                c.Emit(OpCodes.Ldloc, tempFloat);
                c.Emit<SpawnerChances>(OpCodes.Stfld, replaceField.Name + "Chance");
            }
        }
    }
}