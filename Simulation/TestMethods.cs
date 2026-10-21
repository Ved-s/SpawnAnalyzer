using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using SpawnAnalyzer.Rewriters.SpawnAnNPC;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;

namespace SpawnAnalyzer.Simulation;

using Utils = Terraria.Utils;

#pragma warning disable IDE0060 // Remove unused parameter

public static class TestMethods
{
    public static MethodInfo? GetTestMethodInfo(int number)
    {
        return typeof(TestMethods).GetMethod($"TestMethod{number}", (BindingFlags)(-1));
    }

    public static TestBlock[]? GetExpectedTestResults(int number)
    {
        return typeof(TestMethods).GetField($"TestMethod{number}ExpectedTestResults", (BindingFlags)(-1))?.GetValue(null) as TestBlock[];
    }

    public static void PrepareSimulationForTest(int number, Simulator ctx)
    {
        typeof(TestMethods).GetMethod($"TestMethod{number}InitSimulation", (BindingFlags)(-1))?.Invoke(null, [ctx]);
    }

    public class TestBlock
    {
        public int[] spawns;

        public (float, int?)[] branches;
        public TestBlock(int[] spawns, (float, int?)[] nextBlocks)
        {
            this.spawns = spawns;
            this.branches = nextBlocks;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestInlineAttribute : Attribute { }

    public static TestBlock[] TestMethod1ExpectedTestResults = [
        /* 0 */ new([], [(1f/7, 1), (6f/7, 4)]),
        /* 1 */ new([], [(1f/10, 2), (9f/10, 3)]),
        /* 2 */ new([100], []),
        /* 3 */ new([145], []),
        /* 4 */ new([], [(1f/3, 5), (2f/3, 6)]),
        /* 5 */ new([143], []),
        /* 6 */ new([], [(1f/2, 7), (1f/2, null)]),
        /* 7 */ new([144], []),
    ];

    public static void TestMethod1(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        if (Main.rand.Next(7) == 0)
        {
            // 1
            if (Main.rand.Next(10) == 0)
            {
                // 2
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 100, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            // 3

            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 145, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        // 4
        if (Main.rand.Next(3) == 0) // 2
        {
            // 5
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 143, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        // 6
        if (Main.rand.Next(2) == 0) // 3
        {
            // 7
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 144, 0, 0f, 0f, 0f, 0f, 255);
        }
        return;
    }
    
    public static TestBlock[] TestMethod2ExpectedTestResults = [
        /* 0 */ new([], [
            (1f/4, 1),
            (1f/4, 2),
            (1f/4, 3),
            (1f/4, 4),
        ]),
        /* 1 */ new([101], []),
        /* 2 */ new([102], []),
        /* 3 */ new([103], []),
        /* 4 */ new([104], []),
    ];

    public static void TestMethod2(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        int id = Main.rand.Next(101, 105);

        // 1 2 3 4
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, id, 0, 0f, 0f, 0f, 0f, 255);
        return;
    }
    
    public static TestBlock[] TestMethod3ExpectedTestResults = [
        /* 0 */ new([], [
            (1f/8, 1),
            (1f/8, 1),
            (1f/8, 1),
            (1f/8, 2),
            (1f/8, 2),
            (1f/8, 3),
            (1f/8, 4),
            (1f/8, 5),
        ]),
        /* 1 */ new([411], []),
        /* 2 */ new([409], []),
        /* 3 */ new([407], []),
        /* 4 */ new([402], []),
        /* 5 */ new([405], []),
    ];

    public static void TestMethod3(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        int num8 = Utils.SelectRandom(Main.rand, [411, 411, 411, 409, 409, 407, 402, 405]);
        if (num8 != 0)
        {
            // 1 2 3 4 5
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, num8, 1, 0f, 0f, 0f, 0f, 255);
            return;
        }
        return;
    }

    
    public static TestBlock[] TestMethod4ExpectedTestResults = [
        /* 0 */ new([], [
            (1f/5, 1),
            (1f/5, 2),
            (1f/5, 3),
            (1f/5, 4),
            (1f/5, 5),
        ]),
        /* 1 */ new([524], []),
        /* 2 */ new([524], []),
        /* 3 */ new([530], []),
        /* 4 */ new([528], []),
        /* 5 */ new([532], []),
    ];

    public static void TestMethod4(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        List<int> list = new List<int>();
        if (spawner.ZoneCorrupt)
        {
            list.Add(525);
            list.Add(525);
        }
        if (spawner.ZoneCrimson)
        {
            list.Add(526);
            list.Add(526);
        }
        if (spawner.ZoneHallow)
        {
            list.Add(527);
            list.Add(527);
        }
        if (list.Count == 0)
        {
            list.Add(524);
            list.Add(524);
        }
        if (spawner.ZoneCorrupt || spawner.ZoneCrimson)
        {
            list.Add(533);
            list.Add(529);
        }
        else
        {
            list.Add(530);
            list.Add(528);
        }
        list.Add(532);
        int num18 = Utils.SelectRandom<int>(Main.rand, list.ToArray());
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, num18, 0, 0f, 0f, 0f, 0f, 255);
        list.Clear();
        return;
    }

    public static TestBlock[] TestMethod5ExpectedTestResults = [
        /*  0 */ new([], [
            (1f/7, 1),
            (1f/7, 2),
            (1f/7, 2),
            (1f/7, 3),
            (1f/7, 3),
            (1f/7, 3),
            (1f/7, 3),
        ]),

        /*  1 */ new([], [
            (1f/45, 4),
            (44f/45, 5)
        ]),

        /*  2 */ new([], [
            (1f/45, 4),
            (44f/45, 10)
        ]),

        /*  3 */ new([], [
            (1f/45, 4),
            (44f/45, 14)
        ]),

        /*  4 */ new([395], []),

        /*  5 */ new([], [
            (1f/20, 6),
            (19f/20, 7),
        ]),
        /*  6 */ new([395], []),
        /*  7 */ new([], [
            (1f/2, 8),
            (1f/2, 9),
        ]),

        /*  8 */ new([390], []),
        /*  9 */ new([386], []),

        /* 10 */ new([], [
            (1f/5, 11),
            (1f/5, 11),
            (1f/5, 12),
            (1f/5, 12),
            (1f/5, 13),
        ]),
        /* 11 */ new([382], []),
        /* 12 */ new([381], []),
        /* 13 */ new([388], []),

        /* 14 */ new([], [
            (1f/3, 15),
            (1f/3, 16),
            (1f/3, 17),
        ]),
        /* 15 */ new([385], []),
        /* 16 */ new([389], []),
        /* 17 */ new([383], []),
    ];

    public static void TestMethod5(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        int num12 = 0;
        int num13 = Main.rand.Next(7);

        // 1 (6) 2 (4, 5) 3 (0, 1, 2, 3)
        if (Main.rand.Next(45) == 0)
        {
            // 4
            num12 = 395;
        }
        else if (num13 >= 6)
        {
            // 5
            if (Main.rand.Next(20) == 0)
            {
                // 6
                num12 = 395;
            }
            else
            {
                // 7
                int num14 = Main.rand.Next(2);
                if (num14 == 0)
                {
                    // 8
                    num12 = 390;
                }
                if (num14 == 1)
                {
                    // 9
                    num12 = 386;
                }
            }
        }
        else if (num13 >= 4)
        {
            // 10
            int num15 = Main.rand.Next(5);
            if (num15 < 2) // 0, 1
            {
                // 11
                num12 = 382;
            }
            else if (num15 < 4) // 2, 3
            {
                // 12
                num12 = 381;
            }
            else // 4
            {
                // 13
                num12 = 388;
            }
        }
        else
        {
            // 14
            int num16 = Main.rand.Next(3);
            if (num16 == 0)
            {
                // 15
                num12 = 385;
            }
            if (num16 == 1)
            {
                // 16
                num12 = 389;
            }
            if (num16 == 2)
            {
                // 17
                num12 = 383;
            }
        }
        if (num12 != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, num12, 1, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }
    
    public static TestBlock[] TestMethod6ExpectedTestResults = [
        /* 0 */ new([], [
            (1f/3, 1),
            (2f/3, 4),
        ]),
        /* 1 */ new([], [
            (1f/NPC.goldCritterChance, 2),
            (1-1f/NPC.goldCritterChance, 3),
        ]),
        /* 2 */ new([447], []),
        /* 3 */ new([300], []),

        /* 4 */ new([], [
            (1f/2, 5),
            (1f/2, 6),
        ]),
        /* 5 */ new([359], []),
        /* 6 */ new([], [
            (1f/NPC.goldCritterChance, 7),
            (1-1f/NPC.goldCritterChance, 8),
        ]),
        /* 7 */ new([448], []),
        /* 8 */ new([], [
            (2f/3, 9),
            (1f/3, null)
        ]),
        /* 9 */ new([357], []),
    ];

    public static void TestMethod6(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        if (Main.rand.Next(3) == 0)
        {
            // 1
            if (spawner.RollLuck(NPC.goldCritterChance) == 0)
            {
                // 2
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 447, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            // 3
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 300, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        else
        {
            // 4
            if (Main.rand.Next(2) == 0)
            {
                // 5
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 359, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            // 6
            if (spawner.RollLuck(NPC.goldCritterChance) == 0)
            {
                // 7
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 448, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            // 8
            if (Main.rand.Next(3) != 0)
            {
                // 9
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 357, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
        }
    }
    
    public static TestBlock[] TestMethod7ExpectedTestResults = [
        /* 0 */ new([], [(1f/3, 1), (2f/3, 4)]),
        /* 1 */ new([], [(1f/2, 2), (1f/2, 3)]),
        /* 2 */ new([299], []),
        /* 3 */ new([538], []),
        /* 4 */ new([46], []),
    ];

    public static void TestMethod7(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        // 0
        if (Main.rand.Next(3) == 0)
        {
            // 1
            spawner.SpawnNPC(
                spawnTileX * 16 + 8,
                spawnTileY * 16,
                Utils.SelectRandom(Main.rand, new int[] { 299, 538 }), // 2 3
                0, 0f, 0f, 0f, 0f, 255
            );
            return;
        }
        // 4
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 46, 0, 0f, 0f, 0f, 0f, 255);
    }

    /*
    public static TestNode[] TestMethod8ExpectedTestResults = [
        new([ // 0
            (1f/2, [624], null),
            (1f/2, [], 1),
        ]),

        new([ // 1
            (1f/NPC.goldCritterChance, [443], null),
            (1 - (1f/NPC.goldCritterChance), [], 2),
        ]),

        new([ // 2
            (1f/NPC.goldCritterChance, [539], null),
            (1 - (1f/NPC.goldCritterChance), [], 3),
        ]),

        new([ // 3
            (1f/3, [], 4),
            (2f/3, [46], null),
        ]),

        new([ // 4
            (1f/2, [299], null),
            (1f/2, [538], null),
        ]),
    ];

    public static void TestMethod8(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (spawner.RollLuck(2) == 0) // 0
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 624, 0, 0f, 0f, 0f, 0f, 255).timeLeft *= 10;
            return;
        }
        if (spawner.RollLuck(NPC.goldCritterChance) == 0) // 1
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 443, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (spawner.RollLuck(NPC.goldCritterChance) == 0) // 2
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 539, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.halloween && Main.rand.Next(3) != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 303, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.xMas && Main.rand.Next(3) != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 337, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (BirthdayParty.PartyIsUp && Main.rand.Next(3) != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 540, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.rand.Next(3) == 0) // 3
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, (int)Utils.SelectRandom<short>(Main.rand, new short[] { 299, 538 }), 0, 0f, 0f, 0f, 0f, 255); // 4
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 46, 0, 0f, 0f, 0f, 0f, 255);
        return;
    }

    public static TestNode[] TestMethod9ExpectedTestResults = [
        new([ // 0
            (30f/100, [1], null),
            (70f/100, [], 1),
        ]),

        new([ // 1
            (70f/100, [2], null),
            (30f/100, [], null),
        ]),
    ];

    public static void TestMethod9(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (Main.rand.Next(100) < 30) // 0
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }

        if (Main.rand.Next(100) >= 30) // 1
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 2, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }

    public static void TestMethod10InitSimulation(Simulator ctx)
    {
        ((SpawnAnNpcStaticData)ctx.staticSimData).chances.dayTimeChance = 0.5f;
    }

    public static TestNode[] TestMethod10ExpectedTestResults = [
        new([ // 0
            (0.5f, [], 1), // true
            (0.5f, [], 2), // false
        ]),
        new([ // 1
            (1f/3, [1], null),
            (2f/3, [], null),
        ]),
        new([ // 2
            (1f/3, [2], null),
            (2f/3, [], null),
        ]),
    ];

    public static void TestMethod10(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (spawner.dayTime        // 0
         && Main.rand.Next(3) == 0 // 1
        )
        {
            if (!spawner.dayTime) // should never be true
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, -1, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }

            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }

        if (!spawner.dayTime       // 2 (dayTime = true) 3 (dayTime = false)
         && Main.rand.Next(3) == 0 // 4
        )
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 2, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }

    public static TestNode[] TestMethod11ExpectedTestResults = [
        new([ // 0
            (0.5f, [], 1), // ret 0
            (0.5f, [], 4), // ret 1
        ]),

        new([ // 1
            (0.5f, [], 2), // ret 6
            (0.5f, [], 3), // ret 7
        ]),

        new([ // 2
            (0.5f, [12], null), // ret 6
            (0.5f, [13], null), // ret 7
        ]),

        new([ // 3
            (0.5f, [13], null), // ret 6
            (0.5f, [14], null), // ret 7
        ]),

        new([ // 4
            (0.5f, [], 5), // ret 0
            (0.5f, [], 6), // ret 1
        ]),

        new([ // 5
            (0.5f, [6], null), // ret 6
            (0.5f, [7], null), // ret 7
        ]),

        new([ // 6
            (0.5f, [7], null), // ret 6
            (0.5f, [8], null), // ret 7
        ]),
    ];

    [TestInline]
    public static int TestMethod11Helper(bool b, int v)
    {
        if (b && v >= 1)
        {
            return Main.rand.Next(2); // 4
        }
        else
        {
            return Main.rand.Next(6, 8); // 1 (b = true), 2 (b = false, x = 6), 3 (b = false, x = 7), 5 (b = false, x = 0), 6 (b = false, x = 1)
        }
    }

    public static void TestMethod11(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        int v = Main.rand.Next(2); // 0

        int x = TestMethod11Helper(true, v);
        int y = TestMethod11Helper(false, v);

        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, x + y, 0, 0f, 0f, 0f, 0f, 255);
    }

    public static void TestMethod12(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (spawner.RollLuck(NPC.goldCritterChance) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 601, 0, 0f, 0f, 0f, 0f, 255);
        }
        else
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, Utils.SelectRandom(Main.rand, [NPCID.RedDragonfly, NPCID.BlueDragonfly]), 0, 0f, 0f, 0f, 0f, 255);
        }
        if (Main.rand.Next(3) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8 - 16, spawnTileY * 16, Utils.SelectRandom(Main.rand, [NPCID.RedDragonfly, NPCID.BlueDragonfly]), 0, 0f, 0f, 0f, 0f, 255);
        }
        if (Main.rand.Next(3) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8 + 16, spawnTileY * 16, Utils.SelectRandom(Main.rand, [NPCID.RedDragonfly, NPCID.BlueDragonfly]), 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }

    public static void TestMethod13(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (Main.rand.Next(5) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, NPC.Spawner.GetGemSquirrelToSpawn(), 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 230, 0, 0f, 0f, 0f, 0f, 255);
    }

    public static void TestMethod14InitSimulation(Simulator ctx)
    {
        Main.tenthAnniversaryWorld = true;
        ((SpawnAnNpcStaticData)ctx.staticSimData).spawner.luck = 0;
    }

    public static TestNode[] TestMethod14ExpectedTestResults = [
        new([ // 0
            (0.5f, [], 1),
            (0.5f, [], 1),
        ]),
        new([ // 1
            (1f/180, [], 3),
            (1-(1f/180), [], 2),
        ]),
        new([ // 2
            (1f/180, [667], null),
            (1-(1f/180), [1], null),
        ]),
        new([ // 3
            (1f/180, [667], null),
            (1-(1f/180), [-4], null),
        ]),
    ];

    public static void TestMethod14(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, bool xRange, int target)
    {
        if (Main.rand.Next(2) == 0) {} // 0
        //
        //  if (NPCID.FromNetId(Type) == 1)
        //  {
        //      if (this.RollLuck(180) == 0) // 1
        //      {
        //          Type = -4;
        //      }
        //      if (Main.tenthAnniversaryWorld && this.RollLuck(180) == 0) // 2, 3 when Type = -4
        //      {
        //          Type = 667;
        //      }
        //  }
        //
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
    }

    */
}