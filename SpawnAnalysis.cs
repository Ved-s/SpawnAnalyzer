using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace SpawnAnalyzer;

class SpawnAnalysis
{
    Rectangle spawnArea;
    Rectangle safeArea;

    int currentMouseOverPage = 0;
    int mouseOverPages = 0;
    const int LinesPerMouseOverPage = 10;

    MouseState? oldMouseState;

    public readonly Dictionary<Point, SpawnAnalysisSpot> foundSpawnSpots = new();

    public readonly Dictionary<Point, SpawnAnalysisResult> results = new();

    public readonly NPC.Spawner globalSpawner = new();

    public readonly SpawnerChances globalSpawnerChances;

    public readonly int spawnRate;
    public readonly int maxSpawns;

    public SpawnAnalysis(Player player)
    {
        Stopwatch sw = Stopwatch.StartNew();
        NPC.Spawner.GetSpawnArea(player, out spawnArea, out safeArea);

        Utils.GetMethodOrThrow<NPC.Spawner>("SetSpawnFlags").Invoke(globalSpawner, [player]);

        globalSpawnerChances = SpawnerChances.WithValuesFrom(globalSpawner);

        SpawnAnalyzer.GetSpawnRateImpl(globalSpawner, player, out spawnRate, out maxSpawns, globalSpawnerChances);

        // Seems like GetSpawnTileParams only outputs one type of spawn params per tile, pick the first
        // and show warnings on multiple different

        bool warning = false;

        for (int y = spawnArea.Top; y < spawnArea.Bottom; y++)
        {
            for (int x = spawnArea.Left; x < spawnArea.Right; x++)
            {
                int xRef = x;
                int yRef = y;
                if (SpawnAnalyzer.GetSpawnTileParamsImpl(globalSpawner, player, ref xRef, ref yRef, spawnArea, safeArea, out SpawnParamsStage1 spawnParams))
                {
                    if (!foundSpawnSpots.TryGetValue(new(xRef, yRef), out SpawnAnalysisSpot? spot))
                    {
                        foundSpawnSpots.Add(new(xRef, yRef), new SpawnAnalysisSpot(this, new(xRef, yRef), spawnParams));
                        continue;
                    }

                    if (!warning && spot.GetStage1Params() != spawnParams)
                    {
                        warning = true;
                        Console.WriteLine("Warning: GetSpawnTileParams generated multiple parameter candidates for the same tile, ignoring new parameters");
                    }
                }
            }
        }

        sw.Stop();
        Console.WriteLine($"Found {foundSpawnSpots.Count} spawn spots in {sw.Elapsed.TotalMilliseconds:0.00}ms");
        Simulate();
    }

    public void Simulate()
    {
        results.Clear();

        Stopwatch sw = Stopwatch.StartNew();
        foreach (SpawnAnalysisSpot spot in foundSpawnSpots.Values)
        {
            spot.Simulate();
        }
        sw.Stop();
        Console.WriteLine($"Simulated {foundSpawnSpots.Count} spawn spots in {sw.Elapsed.TotalMilliseconds:0.00}ms");
    }

    public void DrawOverlay(SpriteBatch sb)
    {
        Rectangle spawnAreaScreen = new(
            (int)(spawnArea.X * 16 - Main.screenPosition.X),
            (int)(spawnArea.Y * 16 - Main.screenPosition.Y),
            spawnArea.Width * 16, spawnArea.Height * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, spawnAreaScreen, Color.Green * 0.1f);

        Rectangle safeAreaScreen = new(
            (int)(safeArea.X * 16 - Main.screenPosition.X),
            (int)(safeArea.Y * 16 - Main.screenPosition.Y),
            safeArea.Width * 16, safeArea.Height * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, safeAreaScreen, Color.Yellow * 0.1f);

        Rectangle playerScreen = new(
            (int)(globalSpawner.pX * 16 - Main.screenPosition.X),
            (int)(globalSpawner.pY * 16 - Main.screenPosition.Y),
            2 * 16, 3 * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, playerScreen, Color.Red * 0.2f);

        foreach (KeyValuePair<Point, SpawnAnalysisSpot> kvp in foundSpawnSpots)
        {
            Rectangle rect = new(
                (int)(kvp.Key.X * 16 - Main.screenPosition.X),
                (int)(kvp.Key.Y * 16 - Main.screenPosition.Y),
                16, 16
            );
            sb.Draw(TextureAssets.MagicPixel.Value, rect, Color.Lime * 0.5f);
        }

        foreach (KeyValuePair<Point, SpawnAnalysisResult> kvp in results)
        {
            Rectangle rect = new(
                (int)(kvp.Key.X * 16 - Main.screenPosition.X),
                (int)(kvp.Key.Y * 16 - Main.screenPosition.Y),
                16, 16
            );
            sb.Draw(TextureAssets.MagicPixel.Value, rect, Color.Yellow * 0.5f);
        }
    }

    internal void MouseOver()
    {
        StringBuilder mouseOverText = new();
        // if (foundSpawnSpots.TryGetValue((Main.MouseWorld / 16).ToPoint(), out var spawnSpot))
        // {
        //     spawnSpot.MouseOver(mouseOverText);
        // }

        if (results.TryGetValue((Main.MouseWorld / 16).ToPoint(), out var spawnResult))
        {
            spawnResult.MouseOver(mouseOverText);
        }

        if (mouseOverText.Length > 0)
        {
            string str = mouseOverText.ToString();
            int lineCount = str.Count(c => c == '\n');

            if (!str.EndsWith('\n'))
                lineCount++;

            if (lineCount > LinesPerMouseOverPage)
            {
                mouseOverPages = (int)Math.Ceiling((double)lineCount / LinesPerMouseOverPage);
                currentMouseOverPage = currentMouseOverPage % mouseOverPages;

                MouseState newMouseState = Mouse.GetState();

                if (oldMouseState is not null)
                {
                    int scrollDiff = oldMouseState.Value.ScrollWheelValue - newMouseState.ScrollWheelValue;
                    if (scrollDiff > 0)
                    {
                        currentMouseOverPage++;
                    }
                    else if (scrollDiff < 0)
                    {
                        currentMouseOverPage--;
                    }

                    if (currentMouseOverPage < 0)
                    {
                        currentMouseOverPage = mouseOverPages - 1;
                    }
                    else if (currentMouseOverPage >= mouseOverPages)
                    {
                        currentMouseOverPage = 0;
                    }
                }
                oldMouseState = newMouseState;

                int startLine = currentMouseOverPage * LinesPerMouseOverPage;
                int endLine = (currentMouseOverPage+1) * LinesPerMouseOverPage;

                int startPos = 0;
                int endPos = str.Length;

                int line = 0;
                for (int i = 0; i < str.Length; i++)
                {
                    if (str[i] != '\n')
                        continue;

                    line++;

                    if (line == startLine)
                    {
                        startPos = i+1;
                    }
                    if (line == endLine)
                    {
                        endPos = i;
                        break;
                    }
                }

                str = str[startPos .. endPos].TrimEnd();
                str = $"{str}\n--- Page {currentMouseOverPage+1}/{mouseOverPages}, use mouse wheel to scroll";
            }

            Main.instance.MouseTextHackZoom(str, 0);
            Main.mouseText = true;
        }
    }
}

class SpawnAnalysisResult
{
    public Dictionary<int, SpawnAnalysisResultSpawn> spawns = new();

    public void MouseOver(StringBuilder text)
    {
        foreach (KeyValuePair<int, SpawnAnalysisResultSpawn> kvp in spawns.OrderByDescending(p => p.Value.chance + p.Value.leakedSpawnChance))
        {
            var (type, spawn) = kvp;

            if (spawn.leakedSpawnCount > 0)
            {
                text.Append("[c/ff0000:");
                if (spawn.count == 0)
                {
                    text.Append("< ");
                    text.Append((spawn.leakedSpawnChance*100).ToString("0.000"));
                    text.Append('%');
                }
                else
                {
                    text.Append("> ");
                    text.Append((spawn.chance*100).ToString("0.000"));
                    text.Append('%');
                }
                text.Append(']');
            }
            else
            {
                text.Append((spawn.chance*100).ToString("0.000"));
                text.Append('%');
            }

            int totalcount = spawn.count + spawn.leakedSpawnCount;

            text.Append(" : ");
            
            if (NPCID.Search.TryGetName(type, out string name))
            {
                text.Append(name);
            }
            else
            {
                text.Append("Id ");
                text.Append(type);
            }
            text.AppendLine();
        }
    }
}

class SpawnAnalysisResultSpawn
{
    public float chance;
    public int count;

    public float leakedSpawnChance;
    public int leakedSpawnCount;

    public bool dependsOnLuck;
}