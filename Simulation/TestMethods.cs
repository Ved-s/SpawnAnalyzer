using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
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

    public static TestNode[]? GetExpectedTestResults(int number)
    {
        return typeof(TestMethods).GetField($"TestMethod{number}ExpectedTestResults", (BindingFlags)(-1))?.GetValue(null) as TestNode[];
    }

    public static void PrepareSimulationForTest(int number, SpawnSimulationContext ctx)
    {
        typeof(TestMethods).GetMethod($"TestMethod{number}InitSimulation", (BindingFlags)(-1))?.Invoke(null, [ctx]);
    }

    public class TestNode
    {
        // (chance, spawns, next)
        public (float, int[], int?)[] branches;

        public TestNode((float, int[], int?)[] branches)
        {
            this.branches = branches;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestInlineAttribute : Attribute { }

    public static TestNode[] TestMethod1ExpectedTestResults = [
        new([ // 0
            (1f/7, [], 1),
            (6f/7, [], 2),
        ]),
        new([ // 1
            (1f/10, [1], null),
            (9f/10, [145], null),
        ]),
        new([ // 2
            (1f/3, [143], null),
            (2f/3, [], 3),
        ]),
        new([ // 3
            (1f/2, [144], null),
            (1f/2, [], null),
        ]),
    ];

    public static void TestMethod1(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(7) == 0) // 0
        {
            if (Main.rand.Next(10) == 0) // 1
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }

            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 145, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.rand.Next(3) == 0) // 2
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 143, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.rand.Next(2) == 0) // 3
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 144, 0, 0f, 0f, 0f, 0f, 255);
        }
        return;
    }

    public static TestNode[] TestMethod2ExpectedTestResults = [
        new([
            (1f/4, [1], null),
            (1f/4, [2], null),
            (1f/4, [3], null),
            (1f/4, [4], null),
        ]),
    ];

    public static void TestMethod2(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int id = Main.rand.Next(1, 5);
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, id, 0, 0f, 0f, 0f, 0f, 255);
        return;
    }

    public static TestNode[] TestMethod3ExpectedTestResults = [
        new([
            (1f/8, [411], null),
            (1f/8, [411], null),
            (1f/8, [411], null),
            (1f/8, [409], null),
            (1f/8, [409], null),
            (1f/8, [407], null),
            (1f/8, [402], null),
            (1f/8, [405], null),
        ]),
    ];

    public static void TestMethod3(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int num8 = Utils.SelectRandom(Main.rand, [411, 411, 411, 409, 409, 407, 402, 405]);
        if (num8 != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, num8, 1, 0f, 0f, 0f, 0f, 255);
            return;
        }
        return;
    }

    public static TestNode[] TestMethod4ExpectedTestResults = [
        new([
            (1f/5, [524], null),
            (1f/5, [524], null),
            (1f/5, [530], null),
            (1f/5, [528], null),
            (1f/5, [532], null),
        ]),
    ];

    public static void TestMethod4(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
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

    public static TestNode[] TestMethod5ExpectedTestResults = [
        new([ // 0
            (1f/7, [], 1),
            (1f/7, [], 1),
            (1f/7, [], 1),
            (1f/7, [], 1),
            (1f/7, [], 2),
            (1f/7, [], 2),
            (1f/7, [], 3),
        ]),
        new([ // 1
            (1f/45, [395], null),
            (44f/45, [], 7),
        ]),
        new([ // 2
            (1f/45, [395], null),
            (44f/45, [], 6),
        ]),
        new([ // 3
            (1f/45, [395], null),
            (44f/45, [], 4),
        ]),
        new([ // 4
            (1f/20, [395], null),
            (19f/20, [], 5),
        ]),
        new([ // 5
            (1f/2, [390], null),
            (1f/2, [386], null),
        ]),
        new([ // 6
            (1f/5, [382], null),
            (1f/5, [382], null),
            (1f/5, [381], null),
            (1f/5, [381], null),
            (1f/5, [388], null),
        ]),
        new([ // 6
            (1f/3, [385], null),
            (1f/3, [389], null),
            (1f/3, [383], null),
        ]),
    ];

    public static void TestMethod5(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int num12 = 0;
        int num13 = Main.rand.Next(7); // 0 
        if (Main.rand.Next(45) == 0) // 1 2 3
        {
            num12 = 395;
        }
        else if (num13 >= 6)
        {
            if (Main.rand.Next(20) == 0)  // 4
            {
                num12 = 395;
            }
            else
            {
                int num14 = Main.rand.Next(2);  // 5
                if (num14 == 0)
                {
                    num12 = 390;
                }
                if (num14 == 1)
                {
                    num12 = 386;
                }
            }
        }
        else if (num13 >= 4)
        {
            int num15 = Main.rand.Next(5); // 6
            if (num15 < 2)
            {
                num12 = 382;
            }
            else if (num15 < 4)
            {
                num12 = 381;
            }
            else
            {
                num12 = 388;
            }
        }
        else
        {
            int num16 = Main.rand.Next(3); // 7
            if (num16 == 0)
            {
                num12 = 385;
            }
            if (num16 == 1)
            {
                num12 = 389;
            }
            if (num16 == 2)
            {
                num12 = 383;
            }
        }
        if (num12 != 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, num12, 1, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }

    public static TestNode[] TestMethod6ExpectedTestResults = [
        new([ // 0
            (1f/3, [], 1),
            (2f/3, [], 2),
        ]),
        new([ // 1
            (1f/NPC.goldCritterChance, [447], null),
            (1 - (1f/NPC.goldCritterChance), [300], null),
        ]),
        new([ // 2
            (1f/2, [359], null),
            (1f/2, [], 3),
        ]),
        new([ // 3
            (1f/NPC.goldCritterChance, [448], null),
            (1 - (1f/NPC.goldCritterChance), [], 4),
        ]),
        new([ // 4
            (2f/3, [357], null),
            (1f/3, [], null),
        ]),
    ];

    public static void TestMethod6(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(3) == 0) // 0
        {
            if (spawner.RollLuck(NPC.goldCritterChance) == 0) // 1
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 447, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 300, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        else
        {
            if (Main.rand.Next(2) == 0) // 2
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 359, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            if (spawner.RollLuck(NPC.goldCritterChance) == 0) // 3
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 448, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            if (Main.rand.Next(3) != 0) // 4
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 357, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
        }
    }

    public static TestNode[] TestMethod7ExpectedTestResults = [
        new([ // 0
            (1f/3, [], 1),
            (2f/3, [46], null),
        ]),

        new([ // 1
            (1f/2, [299], null),
            (1f/2, [538], null),
        ]),
    ];

    public static void TestMethod7(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(3) == 0) // 0
        {
            spawner.SpawnNPC(
                spawnTileX * 16 + 8,
                spawnTileY * 16,
                Utils.SelectRandom(Main.rand, new int[] { 299, 538 }), // 1
                0, 0f, 0f, 0f, 0f, 255
            );
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 46, 0, 0f, 0f, 0f, 0f, 255);
    }

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

    public static void TestMethod8(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
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

    public static void TestMethod9(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
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

    public static void TestMethod10InitSimulation(SpawnSimulationContext ctx)
    {
        ctx.chances.dayTimeChance = 0.5f;
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

    public static void TestMethod10(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
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

    public static void TestMethod11(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int v = Main.rand.Next(2); // 0

        int x = TestMethod11Helper(true, v);
        int y = TestMethod11Helper(false, v);

        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, x + y, 0, 0f, 0f, 0f, 0f, 255);
    }

    public static void TestMethod12(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
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

    public static void TestMethod13(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(5) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, NPC.Spawner.GetGemSquirrelToSpawn(), 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 230, 0, 0f, 0f, 0f, 0f, 255);
    }

    public static void TestMethod14InitSimulation(SpawnSimulationContext ctx)
    {
        Main.tenthAnniversaryWorld = true;
        ctx.spawner.luck = 0;
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

    public static void TestMethod14(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(2) == 0) {} // 0
        /*
            if (NPCID.FromNetId(Type) == 1)
            {
                if (this.RollLuck(180) == 0) // 1
                {
                    Type = -4;
                }
                if (Main.tenthAnniversaryWorld && this.RollLuck(180) == 0) // 2, 3 when Type = -4
                {
                    Type = 667;
                }
            }
        */
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
    }
}