using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;

namespace SpawnAnalyzer.Rewriters;

class SetSpawnFlagsForChosenTileRewriter
{
    public static SpawnAnalyzer.SetSpawnFlagsForChosenTile GenerateMethod()
    {
        DynamicMethodDefinition dmd = new(Utils.GetMethodOrThrow<NPC.Spawner>("SetSpawnFlagsForChosenTile",
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
            ]
        ));

        ILContext il = new(dmd.Definition);
        il.Invoke(RewriteMethod);

        DMDHack.SetNullOriginalMethod(dmd);

        MethodInfo method = dmd.Generate();

        return method.CreateDelegate<SpawnAnalyzer.SetSpawnFlagsForChosenTile>();
    }

    static void RewriteMethod(ILContext il)
    {
        TypeReference spawnerChancesType = il.Import(typeof(SpawnerChances));

        ParameterDefinition spawnerChancesParam = new("spawnParams", Mono.Cecil.ParameterAttributes.None, spawnerChancesType);

        ILCursor c = new(il);

        Func<Instruction, bool>[] marbleGraniteTilexPosMatcher = [
            x=>x.MatchLdarg(1)
        ];
        Func<Instruction, bool>[] marbleGraniteTileyPosMatcher = [
            x=>x.MatchLdarg(2)
        ];
        Func<Instruction, bool>[] marbleGranitePlayerxPosMatcher = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("pX")
        ];
        Func<Instruction, bool>[] marbleGranitePlayeryPosMatcher = [
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("pY")
        ];
        Action<ILCursor> marbleGraniteTileXYEmitter = c =>
        {
            c.Emit(OpCodes.Ldarg_1);
            c.Emit(OpCodes.Ldarg_2);
        };
        Action<ILCursor> marbleGranitePlayerXYEmitter = c =>
        {
            c.Emit(OpCodes.Ldarg_0);
            c.Emit<NPC.Spawner>(OpCodes.Ldfld, "pX");
            c.Emit(OpCodes.Ldarg_0);
            c.Emit<NPC.Spawner>(OpCodes.Ldfld, "pY");
        };

        (Func<Instruction, bool>[], Func<Instruction, bool>[], Action<ILCursor>)[] marbleGraniteReplacers = [
            (marbleGraniteTilexPosMatcher, marbleGraniteTileyPosMatcher, marbleGraniteTileXYEmitter),
            (marbleGranitePlayerxPosMatcher, marbleGranitePlayeryPosMatcher, marbleGranitePlayerXYEmitter),
        ];

        for (int i = 0; i < marbleGraniteReplacers.Length; i++)
        {
            var (xmatch, ymatch, emit) = marbleGraniteReplacers[i];
            if (MatchMarbleGraniteChance(c, xmatch, ymatch) is not MatchMarbleGraniteChanceResult res)
            {
                throw new Exception($"Failed to match marble/granite replacement {i}");
            }

            c.Next!.OpCode = OpCodes.Nop;
            c.Next.Operand = null;
            c.Index += 1;

            c.RemoveRange(res.instructionsMatched - 1);

            emit(c);
            c.Emit(OpCodes.Ldc_I4, res.coordRangeStart);
            c.Emit(OpCodes.Ldc_I4, res.coordRangeEnd);
            c.Emit(OpCodes.Ldc_I4, res.xStepStart);
            c.Emit(OpCodes.Ldc_I4, res.xStepEnd);
            c.Emit(OpCodes.Ldc_I4, res.yStepStart);
            c.Emit(OpCodes.Ldc_I4, res.yStepEnd);
            c.Emit(OpCodes.Ldc_I4, res.width);
            c.Emit(OpCodes.Ldarg, spawnerChancesParam);
            c.Emit<SetSpawnFlagsForChosenTileRewriter>(OpCodes.Call, nameof(CalculateChanceForMarbleAndGranite));
        }

        VariableDefinition tempInt = new(il.Import(typeof(int)));
        il.Body.Variables.Add(tempInt);

        Func<Instruction, bool>[] spiderMatchPre = [];
        Func<Instruction, bool>[] spiderMatchPost = [
            x=>x.MatchLdcI4(WallID.SpiderUnsafe),
            x=>x.MatchBneUn(out _),
        ];
        Func<Instruction, bool>[] desertMatchPre = [
            x=>x.MatchLdsfld(out FieldReference? field) && field!.Name == "AllowsUndergroundDesertEnemiesToSpawn"
        ];
        Func<Instruction, bool>[] desertMatchPost = [
            x=>x.MatchLdelemU1(),
            x=>x.MatchBrfalse(out _),
        ];

        (Func<Instruction, bool>[], Func<Instruction, bool>[], string)[] spiderDesertReplacers = [
            (spiderMatchPre, spiderMatchPost, "spawnSpider"),
            (desertMatchPre, desertMatchPost, "spawnUndergroundDesert"),
        ];

        for (int i = 0; i < spiderDesertReplacers.Length; i++)
        {
            var (pre, post, field) = spiderDesertReplacers[i];

            if (!MatchSpiderOrDesertChance(c, pre, post, field, out int instructionsMatched))
            {
                throw new Exception($"Failed to match spider/desert replacement {i}");
            }

            c.Next!.OpCode = OpCodes.Nop;
            c.Next.Operand = null;
            c.Index += 1;
            c.RemoveRange(instructionsMatched - 1);

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldarg_1);
            c.Emit(OpCodes.Ldarg_2);
            c.Emit(OpCodes.Ldc_I4, i);
            c.Emit(OpCodes.Ldarg, spawnerChancesParam);
            c.Emit<SpawnerChances>(OpCodes.Ldflda, field + "Chance");
            c.Emit<SetSpawnFlagsForChosenTileRewriter>(OpCodes.Call, nameof(CalculateChanceForSpidersAndDeserts));
        }

        /*
                -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
	            -ldc.i4    $chanceDenom
	            -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
	            -brtrue.s  $endif

	            -ldarg.0
	            -ldc.i4.1
	            -stfld     bool Terraria.NPC/Spawner::(?<$field>isOcean|isBeach)
                +ldarg     spawnParamsParam
                +dup
                +ldfld     SpawnParamsStage2::$chanceField
                +ldc.r4    1f/chanceDenom
                +call      float SpawnAnanlyzer::CombineChances(float, float)
                +stfld     SpawnParamsStage2::$chanceField

	        $endif:
        */

        c.Index = 0;
        for (int i = 0; i < 2; i++)
        {
            ILLabel endif = null!;
            int chanceDenom = 0;
            FieldReference field = null!;
            if (!c.TryGotoNext(
                x => x.MatchLdsfld<Main>("rand"),
                x => x.MatchLdcI4(out chanceDenom),
                x => x.MatchCallvirt<UnifiedRandom>("Next"),
                x => x.MatchBrtrue(out endif!),

                x => x.MatchLdarg(0),
                x => x.MatchLdcI4(1),
                x => x.MatchStfld(out field!) && field.DeclaringType.Is(typeof(NPC.Spawner)) && (field.Name is "isBeach" or "isOcean"),
                x => x == endif.Target
            ))
            {
                throw new Exception($"Ocean/beach chance matcher {i} fail");
            }

            FieldReference chanceField = new(field.Name + "Chance", il.Import(typeof(float)), il.Import(typeof(SpawnerChances)));

            c.RemoveRange(7);
            c.Emit(OpCodes.Ldarg, spawnerChancesParam);
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Ldfld, chanceField);
            c.Emit(OpCodes.Ldc_R4, 1f / chanceDenom);
            c.Emit<SpawnAnalyzer>(OpCodes.Call, nameof(SpawnAnalyzer.CombineChances));
            c.Emit(OpCodes.Stfld, chanceField);
        }

        PatchSurfaceSpawnAndDaytimeForRemix(c, spawnerChancesParam);

        // Replace this.field = X; with spawnParams.fieldChance = X;
        List<string> overriddenChanceFieldNames = [];
        foreach (FieldInfo field in typeof(SpawnerChances).GetFields())
        {
            if (field.IsStatic || !field.Name.EndsWith("Chance") || field.FieldType != typeof(float))
                continue;

            string spawnerFieldName = field.Name.Substring(0, field.Name.Length - 6);
            FieldInfo? spawnerField = typeof(NPC.Spawner).GetField(spawnerFieldName, (BindingFlags)(-1));
            if (spawnerField is null)
            {
                throw new Exception($"{nameof(SpawnerChances)}.{field.Name} doesn't have a corresponding Spawner field!");
            }

            if (spawnerField.FieldType != typeof(bool))
            {
                throw new Exception($"Spawner.{spawnerField.Name} has invalid type: {spawnerField.FieldType}!");
            }

            overriddenChanceFieldNames.Add(spawnerFieldName);
        }

        c.Index = 0;
        VariableDefinition tempFloat = new(il.Import(typeof(float)));
        il.Body.Variables.Add(tempFloat);
        FieldReference replaceField = null!;
        while (c.TryGotoNext(
            x => x.MatchStfld(out replaceField!) && replaceField.DeclaringType.Is(typeof(NPC.Spawner)) && overriddenChanceFieldNames.Contains(replaceField.Name)
        ))
        {
            if (c.Prev.MatchLdcI4(out int fieldValue) && c.Instrs[c.Index - 2].MatchLdarg(0))
            {
                c.Index -= 2;

                /*
                    -ldarg     0
                    -ldc.i4    fieldValue
                    -stfld     $replaceField
                    +ldarg     spawnParamsParam
                    +ldc.r4    fieldValue
                    +stfld     SpawnParamsStage2::$replaceFieldChance
                */

                c.Next!.OpCode = OpCodes.Ldarg;
                c.Next.Operand = spawnerChancesParam;
                c.Index += 1;
                c.Next.OpCode = OpCodes.Ldc_R4;
                c.Next.Operand = (float)fieldValue;
                c.Index += 1;
                c.Next.Operand = new FieldReference(replaceField.Name + "Chance", il.Import(typeof(float)), il.Import(typeof(SpawnerChances)));
            }
            else
            {
                /*
                    -stfld     $replaceField
                    +conv.r4
                    +stloc     tempFloat
                    +pop
                    +ldarg     spawnParamsParam
                    +ldloc     tempFloat
                    +stfld     SpawnParamsStage2::$replaceFieldChance
                */
                c.Next!.OpCode = OpCodes.Conv_R4;
                c.Next.Operand = null;
                c.Index += 1;

                c.Emit(OpCodes.Stloc, tempFloat);
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldarg, spawnerChancesParam);
                c.Emit(OpCodes.Ldloc, tempFloat);
                c.Emit<SpawnerChances>(OpCodes.Stfld, replaceField.Name + "Chance");
            }
        }

        foreach (Instruction instr in il.Instrs)
        {
            if (instr.Operand is FieldReference field && field.DeclaringType.Is(typeof(NPC.Spawner)) && overriddenChanceFieldNames.Contains(field.Name))
            {
                throw new Exception($"Found instruction referencing overridden field: {instr}");
            }
        }

        /*
            -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            +pop
            +stloc     tempInt
            +pop
            +ldloc     tempInt

            -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            +pop
            +pop
            +ldc.i4.0
        */
        c.Index = 0;
        while (c.TryGotoNext(
            x => x.MatchCallOrCallvirt<UnifiedRandom>("Next")
        ))
        {
            MethodReference method = (MethodReference)c.Next!.Operand!;
            Console.WriteLine($"Warning! Replacing random.Next in {c.Next}");
            switch (method.Parameters.Count)
            {
                case 1:
                    c.Next.OpCode = OpCodes.Pop;
                    c.Next.Operand = null;
                    c.Index += 1;
                    c.Emit(OpCodes.Pop);
                    c.Emit(OpCodes.Ldc_I4_0);
                    break;

                case 2:
                    c.Next.OpCode = OpCodes.Pop;
                    c.Next.Operand = null;
                    c.Index += 1;
                    c.Emit(OpCodes.Stloc, tempInt);
                    c.Emit(OpCodes.Pop);
                    c.Emit(OpCodes.Ldloc, tempInt);
                    break;

                default:
                    throw new Exception("Unknown Next method!");
            }
        }

        il.Method.IsStatic = true;
        il.Method.HasThis = false;

        il.Method.Parameters.Add(spawnerChancesParam);
    }

    struct MatchMarbleGraniteChanceResult
    {
        public int instructionsMatched;
        public int coordRangeStart;
        public int coordRangeEnd;
        public int xStepStart;
        public int xStepEnd;
        public int yStepStart;
        public int yStepEnd;
        public int width;
    }
    
    static MatchMarbleGraniteChanceResult? MatchMarbleGraniteChance(
        ILCursor c,
        Func<Instruction, bool>[] xPosMatcher,
        Func<Instruction, bool>[] yPosMatcher
    )
    {
        MatchMarbleGraniteChanceResult res = default;

        c.Index = 0;

        /*
            ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            ldc.i4.s  coordRangeStart
            ldc.i4.s  coordRangeEnd
            callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            stloc	  coordRange
            ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            ldc.i4    xStepStart
            ldc.i4    xStepEnd
            callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            stloc	  xStep
            
            $xPosMatcher

            ldloc	  coordRange
            sub
            ldc.i4.0
            bge.s     IL_0121
            
            $xPosMatcher

            stloc	  coordRange
            
            $yPosMatcher

            ldloc	  coordRange
            sub
            ldc.i4.0
            bge.s     IL_0129
            
            $yPosMatcher

            stloc	  coordRange
            
            $xPosMatcher

            ldloc	  coordRange
            add
            ldsfld    int32 Terraria.Main::maxTilesX
            blt.s     IL_013D
            ldsfld    int32 Terraria.Main::maxTilesX
            
            $xPosMatcher

            sub
            ldc.i4    width
            sub
            stloc	  coordRange
            
            $yPosMatcher

            ldloc	  coordRange
            add
            ldsfld    int32 Terraria.Main::maxTilesY
            blt.s     IL_0151
            ldsfld    int32 Terraria.Main::maxTilesY
            
            $yPosMatcher

            sub
            ldc.i4    width
            sub
            stloc	  coordRange
            
            $xPosMatcher

            ldloc	  coordRange
            sub
            stloc.s   xCoord
            br.s      IL_01C3
            ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            ldc.i4.1
            ldc.i4.4
            callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            stloc.s   yStep
            
            $yPosMatcher

            ldloc	  coordRange
            sub
            stloc.s   yCoord
            br.s      IL_01B6

            ldsfld    class Terraria.Tile[0..., 0...] Terraria.Main::tile
            ldloc.s   xCoord
            ldloc.s   yCoord
            call      instance class Terraria.Tile class Terraria.Tile[0..., 0...]::Get(int32, int32)
            ldfld     uint16 Terraria.Tile::'type'
            ldc.i4    367
            bne.un.s  IL_018E

            ldarg.0
            ldc.i4.1
            stfld     bool Terraria.NPC/Spawner::nearMarble

            ldsfld    class Terraria.Tile[0..., 0...] Terraria.Main::tile
            ldloc.s   xCoord
            ldloc.s   yCoord
            call      instance class Terraria.Tile class Terraria.Tile[0..., 0...]::Get(int32, int32)
            ldfld     uint16 Terraria.Tile::'type'
            ldc.i4    368
            bne.un.s  IL_01AF

            ldarg.0
            ldc.i4.1
            stfld     bool Terraria.NPC/Spawner::nearGranite

            ldloc.s   yCoord
            ldloc.s   yStep
            add
            stloc.s   yCoord
            ldloc.s   yCoord
            
            $yPosMatcher

            ldloc	  coordRange
            add
            ble.s     IL_016D
            ldloc.s   xCoord
            ldloc	  xStep
            add
            stloc.s   xCoord
            ldloc.s   xCoord
            
            $xPosMatcher

            ldloc	  coordRange
            add
            ble.s     IL_0158
        */

        List<Func<Instruction, bool>> matchers = [];

        int coordRange = 0;
        int xStep = 0;

        matchers.AddRange([
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out res.coordRangeStart),
            x=>x.MatchLdcI4(out res.coordRangeEnd),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out coordRange),
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out res.xStepStart),
            x=>x.MatchLdcI4(out res.xStepEnd),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out xStep),
        ]);

        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchLdcI4(0),
            x=>x.MatchBge(out _),
        ]);
        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchStloc(coordRange),
        ]);

        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchLdcI4(0),
            x=>x.MatchBge(out _),
        ]);
        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchStloc(coordRange),
        ]);

        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchLdsfld<Main>("maxTilesX"),
            x=>x.MatchBlt(out _),
            x=>x.MatchLdsfld<Main>("maxTilesX"),
        ]);

        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchSub(),
            x=>x.MatchLdcI4(out res.width),
            x=>x.MatchSub(),
            x=>x.MatchStloc(coordRange),
        ]);

        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchLdsfld<Main>("maxTilesY"),
            x=>x.MatchBlt(out _),
            x=>x.MatchLdsfld<Main>("maxTilesY"),
        ]);

        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchSub(),
            x=>x.MatchLdcI4(res.width),
            x=>x.MatchSub(),
            x=>x.MatchStloc(coordRange),
        ]);

        int xCoord = 0;
        int yStep = 0;

        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchStloc(out xCoord),
            x=>x.MatchBr(out _),
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(out res.yStepStart),
            x=>x.MatchLdcI4(out res.yStepEnd),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out yStep),
        ]);

        int yCoord = 0;

        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchStloc(out yCoord),
            x=>x.MatchBr(out _),

            x=>x.MatchLdsfld<Main>("tile"),
            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdloc(yCoord),
            x=>x.MatchCall(out MethodReference? m) && m.Name == "Get",
            x=>x.MatchLdfld<Tile>("type"),
            x=>x.MatchLdcI4(TileID.Marble),
            x=>x.MatchBneUn(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("nearMarble"),

            x=>x.MatchLdsfld<Main>("tile"),
            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdloc(yCoord),
            x=>x.MatchCall(out MethodReference? m) && m.Name == "Get",
            x=>x.MatchLdfld<Tile>("type"),
            x=>x.MatchLdcI4(TileID.Granite),
            x=>x.MatchBneUn(out _),

            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>("nearGranite"),

            x=>x.MatchLdloc(yCoord),
            x=>x.MatchLdloc(yStep),
            x=>x.MatchAdd(),
            x=>x.MatchStloc(yCoord),
            x=>x.MatchLdloc(yCoord),
        ]);

        matchers.AddRange(yPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchBle(out _),
            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdloc(xStep),
            x=>x.MatchAdd(),
            x=>x.MatchStloc(xCoord),
            x=>x.MatchLdloc(xCoord),
        ]);

        matchers.AddRange(xPosMatcher);
        matchers.AddRange([
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchBle(out _),
        ]);

        res.instructionsMatched = matchers.Count;

        if (c.TryGotoNext(matchers.ToArray()))
        {
            return res;
        }
        else return null;

    }

    static void CalculateChanceForMarbleAndGranite(
        int x, int y,
        int coordRangeStart, int coordRangeEnd,
        int xStepStart, int xStepEnd,
        int yStepStart, int yStepEnd,
        int width,
        SpawnerChances p2
    )
    {
        if (p2.nearGraniteChance >= 1 && p2.nearMarbleChance >= 1)
        {
            return;
        }
        ulong checks = 0;
        ulong marbleHits = 0;
        ulong graniteHits = 0;

        for (int coordRange = coordRangeStart; coordRange < coordRangeEnd; coordRange++)
        {
            int coordRangeFixed = coordRange;
            if (x - coordRangeFixed < 0)
            {
                coordRangeFixed = x;
            }
            if (y - coordRangeFixed < 0)
            {
                coordRangeFixed = y;
            }
            if (x + coordRangeFixed >= Main.maxTilesX)
            {
                coordRangeFixed = Main.maxTilesX - x - width;
            }
            if (y + coordRangeFixed >= Main.maxTilesY)
            {
                coordRangeFixed = Main.maxTilesY - y - width;
            }

            for (int xStep = xStepStart; xStep < xStepEnd; xStep++)
                for (int yStep = yStepStart; yStep < yStepEnd; yStep++)
                {
                    bool marbleHit = false;
                    bool graniteHit = false;
                    for (int tx = x - coordRangeFixed; tx <= x + coordRangeFixed; tx += xStep)
                    {
                        for (int ty = y - coordRangeFixed; ty <= y + coordRangeFixed; ty += yStep)
                        {
                            int type = Main.tile[tx, ty].type;
                            if (type == TileID.Marble)
                            {
                                marbleHit = true;
                            }
                            if (type == TileID.Granite)
                            {
                                graniteHit = true;
                            }
                        }
                        if (marbleHit && graniteHit)
                            break;
                    }
                    if (graniteHit)
                    {
                        graniteHits++;
                    }
                    if (marbleHit)
                    {
                        marbleHits++;
                    }
                    checks++;
                }
        }

        float marbleChance = (float)((double)marbleHits / checks);
        float graniteChance = (float)((double)graniteHits / checks);

        p2.nearMarbleChance = SpawnAnalyzer.CombineChances(p2.nearMarbleChance, marbleChance);
        p2.nearGraniteChance = SpawnAnalyzer.CombineChances(p2.nearGraniteChance, graniteChance);
    }

    static bool MatchSpiderOrDesertChance(
        ILCursor c,
        Func<Instruction, bool>[] valueMatcherPre,
        Func<Instruction, bool>[] valueMatcherPost,
        string fieldName,
        out int instructionsMatched
    )
    {
        /*
            ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            ldc.i4.3
            callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
            brtrue.s  _
            ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
            ldc.i4.5
            ldc.i4.s  15
            callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32, int32)
            stloc.s   coordRange

            ldarg.1
            ldloc.s   coordRange
            sub
            ldc.i4.0
            blt.s     _

            ldarg.1
            ldloc.s   coordRange
            add
            ldsfld    int32 Terraria.Main::maxTilesX
            bge.s     _

            ldarg.1
            ldloc.s   coordRange
            sub
            stloc.s   xCoord
            br.s      _

            ldarg.2
            ldloc.s   coordRange
            sub
            stloc.s   yCoord
            br.s      _

            $valueMatcherPre

            ldsfld    class Terraria.Tile[0..., 0...] Terraria.Main::tile
            ldloc.s   xCoord
            ldloc.s   yCoord
            call      instance class Terraria.Tile class Terraria.Tile[0..., 0...]::Get(int32, int32)
            ldfld     uint16 Terraria.Tile::wall
            
            $valueMatcherPost
            
            ldarg.0
            ldc.i4.1
            stfld     bool Terraria.NPC/Spawner::$fieldName

            ldloc.s   yCoord
            ldc.i4.1
            add
            stloc.s   yCoord

            ldloc.s   yCoord
            ldarg.2
            ldloc.s   coordRange
            add
            blt.s     _

            ldloc.s   xCoord
            ldc.i4.1
            add
            stloc.s   xCoord

            ldloc.s   xCoord
            ldarg.1
            ldloc.s   coordRange
            add
            blt.s     _

            br.s      _

            $valueMatcherPre

            ldsfld    class Terraria.Tile[0..., 0...] Terraria.Main::tile
            ldarg.0
            ldfld     int32 Terraria.NPC/Spawner::pX
            ldarg.0
            ldfld     int32 Terraria.NPC/Spawner::pY
            call      instance class Terraria.Tile class Terraria.Tile[0..., 0...]::Get(int32, int32)
            ldfld     uint16 Terraria.Tile::wall
            
            $valueMatcherPost

            ldarg.0
            ldc.i4.1
            stfld     bool Terraria.NPC/Spawner::$fieldName
        */

        List<Func<Instruction, bool>> matchers = [];

        int coordRange = 0;
        int xCoord = 0;
        int yCoord = 0;

        matchers.AddRange([
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(3),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchBrtrue(out _),
            x=>x.MatchLdsfld<Main>("rand"),
            x=>x.MatchLdcI4(5),
            x=>x.MatchLdcI4(15),
            x=>x.MatchCallvirt<UnifiedRandom>("Next"),
            x=>x.MatchStloc(out coordRange),

            x=>x.MatchLdarg(1),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchLdcI4(0),
            x=>x.MatchBlt(out _),

            x=>x.MatchLdarg(1),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchLdsfld<Main>("maxTilesX"),
            x=>x.MatchBge(out _),

            x=>x.MatchLdarg(1),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchStloc(out xCoord),
            x=>x.MatchBr(out _),

            x=>x.MatchLdarg(2),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchSub(),
            x=>x.MatchStloc(out yCoord),
            x=>x.MatchBr(out _),
        ]);
        matchers.AddRange(valueMatcherPre);
        matchers.AddRange([
            x=>x.MatchLdsfld<Main>("tile"),
            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdloc(yCoord),
            x=>x.MatchCall(out MethodReference? m) && m.Name == "Get",
            x=>x.MatchLdfld<Tile>("wall"),
        ]);
        matchers.AddRange(valueMatcherPost);
        matchers.AddRange([
            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>(fieldName),

            x=>x.MatchLdloc(yCoord),
            x=>x.MatchLdcI4(1),
            x=>x.MatchAdd(),
            x=>x.MatchStloc(yCoord),

            x=>x.MatchLdloc(yCoord),
            x=>x.MatchLdarg(2),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchBlt(out _),

            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdcI4(1),
            x=>x.MatchAdd(),
            x=>x.MatchStloc(xCoord),

            x=>x.MatchLdloc(xCoord),
            x=>x.MatchLdarg(1),
            x=>x.MatchLdloc(coordRange),
            x=>x.MatchAdd(),
            x=>x.MatchBlt(out _),

            x=>x.MatchBr(out _),
        ]);
        matchers.AddRange(valueMatcherPre);
        matchers.AddRange([
            x=>x.MatchLdsfld<Main>("tile"),
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("pX"),
            x=>x.MatchLdarg(0),
            x=>x.MatchLdfld<NPC.Spawner>("pY"),
            x=>x.MatchCall(out MethodReference? m) && m.Name == "Get",
            x=>x.MatchLdfld<Tile>("wall"),
        ]);
        matchers.AddRange(valueMatcherPost);

        matchers.AddRange([
            x=>x.MatchLdarg(0),
            x=>x.MatchLdcI4(1),
            x=>x.MatchStfld<NPC.Spawner>(fieldName),
        ]);

        instructionsMatched = matchers.Count;

        c.Index = 0;
        return c.TryGotoNext(matchers.ToArray());
    }

    static void CalculateChanceForSpidersAndDeserts(
        NPC.Spawner spawner,
        int x, int y,
        int rule,
        ref float chance
    )
    {
        if (chance >= 1)
        {
            return;
        }
        ulong checks = 0;
        ulong hits = 0;

        for (int coordRange = 5; coordRange < 15; coordRange++)
        {
            bool hit = false;
            if (x - coordRange >= 0 && x + coordRange < Main.maxTilesX)
            {
                for (int tx = x - coordRange; tx < x + coordRange && !hit; tx++)
                {
                    for (int ty = y - coordRange; ty < y + coordRange; ty++)
                    {
                        switch (rule)
                        {
                            case 0:
                                if (Main.tile[tx, ty].wall == WallID.SpiderUnsafe)
                                {
                                    hit = true;
                                }
                                break;
                            case 1:
                                if (WallID.Sets.AllowsUndergroundDesertEnemiesToSpawn[Main.tile[tx, ty].wall])
                                {
                                    hit = true;
                                }
                                break;
                        }

                    }
                }
            }
            checks++;
            if (hit)
                hits++;
        }

        bool playerHit = false;
        switch (rule)
        {
            case 0:
                if (Main.tile[spawner.pX, spawner.pY].wall == WallID.SpiderUnsafe)
                {
                    playerHit = true;
                }
                break;
            case 1:
                if (WallID.Sets.AllowsUndergroundDesertEnemiesToSpawn[Main.tile[spawner.pX, spawner.pY].wall])
                {
                    playerHit = true;
                }
                break;
        }


        float totalChance;
        if (hits == 0 && !playerHit)
        {
            totalChance = 0;
        }
        else if (hits == checks && playerHit)
        {
            totalChance = 1;
        }
        else
        {
            float chance1 = (float)((double)hits / checks);
            float chance1scaled = chance1 / 3;
            float chance2scaled = 0;
            if (playerHit)
            {
                chance2scaled = 2f / 3;
            }
            totalChance = chance1scaled + chance2scaled;
        }

        chance = SpawnAnalyzer.CombineChances(chance, totalChance);
    }

    static void PatchSurfaceSpawnAndDaytimeForRemix(ILCursor c, ParameterDefinition spawnerChancesParam)
    {
        VariableDefinition firstCondition = new(c.IL.Import(typeof(bool)));
        VariableDefinition secondCondition = new(c.IL.Import(typeof(bool)));

        c.Body.Variables.Add(firstCondition);
        c.Body.Variables.Add(secondCondition);

        ILLabel firstConditionEnd = null!;
        ILLabel secondConditionEnd = null!;
        ILLabel setSecondConditionVar = c.DefineLabel();

        int firstIfRemoveLengthLength = 15;
        int secondBodyLength = 3;

        /*
            if (firstCondition && Main.rand.Next(3) != 0)
            {
				this.surfaceSpawn = true;
				this.dayTime = Main.rand.Next(2) == 0;
			}
			else if (secondCondition)
			{
				this.surfaceSpawn = true;
			}

	        IL_06A7: -bgt.s     firstConditionEnd
                     +cgt
                     +ldc.i4    0
                     +ceq
                     +stloc     firstCondition

          /= firstIfRemoveLengthLength = 15
	      | IL_06A9: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
	      | IL_06AE: -ldc.i4.3
	      | IL_06AF: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
	      | IL_06B4: -brfalse.s firstConditionEnd
          |
	      | IL_06B6: -ldarg.0
	      | IL_06B7: -ldc.i4.1
	      | IL_06B8: -stfld     bool Terraria.NPC/Spawner::surfaceSpawn
	      | IL_06BD: -ldarg.0
	      | IL_06BE: -ldsfld    class Terraria.Utilities.UnifiedRandom Terraria.Main::rand
	      | IL_06C3: -ldc.i4.2
	      | IL_06C4: -callvirt  instance int32 Terraria.Utilities.UnifiedRandom::Next(int32)
	      | IL_06C9: -ldc.i4.0
	      | IL_06CA: -ceq
	      | IL_06CC: -stfld     bool Terraria.NPC/Spawner::dayTime
	      \ IL_06D1: -br.s      secondConditionEnd

            firstConditionEnd:

	    *** goto secondConditionEnd - secondBodyLength - 1 ***

                      anyJumpInstr secondConditionEnd

                     +ldc.i4    1
                     +br        setSecondConditionVar

            +secondConditionEnd:
                     +ldc.i4    0

            setSecondConditionVar:
                     +stloc     secondCondition

          /= secondBodyLength = 3
	      | IL_071A: -ldarg.0
	      | IL_071B: -ldc.i4.1
	      \ IL_071C: -stfld     bool Terraria.NPC/Spawner::surfaceSpawn

                     +ldloc     firstCondition
                     +ldloc     secondCondition
                     +ldarg     spawnParams
                     +call      void CalculateSurfaceSpawnAndDaytimeForRemixChances(bool, bool, SpawnParamsStage2&)

            -secondConditionEnd:
        */

        c.Index = 0;
        if (!c.TryGotoNext(
            x => x.MatchBgt(out firstConditionEnd!),

            x => x.MatchLdsfld<Main>("rand"),
            x => x.MatchLdcI4(3),
            x => x.MatchCallvirt<UnifiedRandom>("Next"),
            x => x.MatchBrfalse(out ILLabel? label) && label.Target == firstConditionEnd.Target,

            x => x.MatchLdarg(0),
            x => x.MatchLdcI4(1),
            x => x.MatchStfld<NPC.Spawner>("surfaceSpawn"),
            x => x.MatchLdarg(0),
            x => x.MatchLdsfld<Main>("rand"),
            x => x.MatchLdcI4(2),
            x => x.MatchCallvirt<UnifiedRandom>("Next"),
            x => x.MatchLdcI4(0),
            x => x.MatchCeq(),
            x => x.MatchStfld<NPC.Spawner>("dayTime"),
            x => x.MatchBr(out secondConditionEnd!),

            x => firstConditionEnd.Target == x
        ))
        {
            throw new Exception("PatchSurfaceSpawnAndDaytimeForRemix initial match fail");
        }
        int beginning = c.Index;
        c.GotoLabel(secondConditionEnd);
        c.Index -= secondBodyLength + 1;
        if (!SpawnAnalyzer.MatchInstructions(
            c.Context, c.Index, out int matchEndPos,
            x => (x.Operand as ILLabel)?.Target == secondConditionEnd.Target,
            x => x.MatchLdarg(0),
            x => x.MatchLdcI4(1),
            x => x.MatchStfld<NPC.Spawner>("surfaceSpawn")
        ))
        {
            throw new Exception($"PatchSurfaceSpawnAndDaytimeForRemix second match fail at IL_{c.Instrs[matchEndPos].Offset:x04}");
        }

        c.Index = beginning;
        c.Next!.OpCode = OpCodes.Cgt;
        c.Next.Operand = null;
        c.Index += 1;

        c.Emit(OpCodes.Ldc_I4_0);
        c.Emit(OpCodes.Ceq);
        c.Emit(OpCodes.Stloc, firstCondition);

        c.RemoveRange(firstIfRemoveLengthLength);

        Debug.Assert(firstConditionEnd.Target == c.Next);

        c.GotoLabel(secondConditionEnd);

        c.Index -= secondBodyLength + 1;
        Debug.Assert(c.Next.Operand == secondConditionEnd);
        c.Index += 1;

        c.Emit(OpCodes.Ldc_I4_1);
        c.Emit(OpCodes.Br, setSecondConditionVar);

        c.Emit(OpCodes.Ldc_I4_0);
        c.RetargetLabel(secondConditionEnd, c.Prev);

        c.MarkLabel(setSecondConditionVar);
        c.Emit(OpCodes.Stloc, secondCondition);

        c.RemoveRange(secondBodyLength);

        c.Emit(OpCodes.Ldloc, firstCondition);
        c.Emit(OpCodes.Ldloc, secondCondition);
        c.Emit(OpCodes.Ldarg, spawnerChancesParam);
        c.Emit<SetSpawnFlagsForChosenTileRewriter>(OpCodes.Call, nameof(CalculateSurfaceSpawnAndDaytimeForRemixChances));
    }

    static void CalculateSurfaceSpawnAndDaytimeForRemixChances(bool firstCondition, bool secondCondition, SpawnerChances p2)
    {
        // TODO: dependent percent rules
        if (firstCondition)
        {
            float thisBranchChance = 2f / 3;
            p2.surfaceSpawnChance = SpawnAnalyzer.CombineChances(p2.surfaceSpawnChance, thisBranchChance);
            p2.dayTimeChance = p2.dayTimeChance * (1f - thisBranchChance) + thisBranchChance * 1f/2;
        }
        if (secondCondition)
        {
            float thisBranchChance = firstCondition ? 1f / 3 : 1f;
            p2.surfaceSpawnChance = SpawnAnalyzer.CombineChances(p2.surfaceSpawnChance, thisBranchChance);
        }

        if (firstCondition && secondCondition)
        {
            p2.surfaceSpawnChance = 1f;
        }
    }
}




