using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Terraria;
using Terraria.GameContent.Events;

#pragma warning disable IDE0060 // Remove unused parameter

public static class TestMethods
{
    public static MethodInfo GetTestMethodInfo(int number)
    {
        return typeof(TestMethods).GetMethod($"TestMethod{number}", (BindingFlags)(-1)) ?? throw new EntryPointNotFoundException();
    }

    public static void TestMethod1(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(7) == 0)
        {
            if (Main.rand.Next(10) == 0)
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }

            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 145, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.rand.Next(3) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 143, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (Main.rand.Next(2) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 144, 0, 0f, 0f, 0f, 0f, 255);
        }
        return;
    }

    public static void TestMethod2(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int id = Main.rand.Next(1, 5);
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, id, 0, 0f, 0f, 0f, 0f, 255);
        return;
    }

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

    public static void TestMethod5(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        int num12 = 0;
        int num13 = Main.rand.Next(7);
        if (Main.rand.Next(45) == 0)
        {
            num12 = 395;
        }
        else if (num13 >= 6)
        {
            if (Main.rand.Next(20) == 0)
            {
                num12 = 395;
            }
            else
            {
                int num14 = Main.rand.Next(2);
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
            int num15 = Main.rand.Next(5);
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
            int num16 = Main.rand.Next(3);
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

    public static void TestMethod6(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(3) == 0)
        {
            if (spawner.RollLuck(NPC.goldCritterChance) == 0)
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 447, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 300, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        else
        {
            if (Main.rand.Next(2) == 0)
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 359, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            if (spawner.RollLuck(NPC.goldCritterChance) == 0)
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 448, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
            if (Main.rand.Next(3) != 0)
            {
                spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 357, 0, 0f, 0f, 0f, 0f, 255);
                return;
            }
        }
    }

    public static void TestMethod7(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(3) == 0)
        {
            spawner.SpawnNPC(
                spawnTileX * 16 + 8, 
                spawnTileY * 16, 
                Utils.SelectRandom(Main.rand, new int[] { 299, 538 }),
                0, 0f, 0f, 0f, 0f, 255
            );
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 46, 0, 0f, 0f, 0f, 0f, 255);
    }

    public static void TestMethod8(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (spawner.RollLuck(2) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 624, 0, 0f, 0f, 0f, 0f, 255).timeLeft *= 10;
            return;
        }
        if (spawner.RollLuck(NPC.goldCritterChance) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 443, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        if (spawner.RollLuck(NPC.goldCritterChance) == 0)
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
        if (Main.rand.Next(3) == 0)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, (int)Utils.SelectRandom<short>(Main.rand, new short[] { 299, 538 }), 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
        spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 46, 0, 0f, 0f, 0f, 0f, 255);
        return;
    }

    public static void TestMethod9(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, bool xRange, int target)
    {
        if (Main.rand.Next(100) < 30)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }

        if (Main.rand.Next(100) >= 30)
        {
            spawner.SpawnNPC(spawnTileX * 16 + 8, spawnTileY * 16, 1, 0, 0f, 0f, 0f, 0f, 255);
            return;
        }
    }
}