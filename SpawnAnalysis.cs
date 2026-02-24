using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using ReLogic.Reflection;
using SpawnAnalyzer.UI;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI.Chat;

namespace SpawnAnalyzer;

public class SpawnAnalysis
{
    public Rectangle spawnArea;
    public Rectangle safeArea;

    public readonly Dictionary<Point, SpawnAnalysisSpot> foundSpawnSpots = new();

    public readonly Dictionary<Point, Dictionary<int, AnalyzedMultiSpawn>> results = new();

    public readonly NPC.Spawner globalSpawner = new();

    public readonly SpawnerChances globalSpawnerChances;

    public readonly int spawnRate;
    public readonly int maxSpawns;

    Point? lastHoveredSpot;

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
                    spot.hits++;

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

        TimeSpan totalAnalysisTime = default;
        Stopwatch sw = Stopwatch.StartNew();
        foreach (SpawnAnalysisSpot spot in foundSpawnSpots.Values)
        {
            spot.Simulate(out var at);
            totalAnalysisTime += at;
        }

        foreach (var posDict in results.Values)
        {
            foreach (var mspawn in posDict.Values)
            {
                mspawn.spawns.Sort((a, b) => Math.Sign(b.chance - a.chance));
            }
        }

        sw.Stop();
        Console.WriteLine($"Simulated {foundSpawnSpots.Count} spawn spots in {sw.Elapsed.TotalMilliseconds:0.00}ms (with {totalAnalysisTime.TotalMilliseconds:0.00}ms spent analyzing)");
    }

    public void DrawOverlay(SpriteBatch sb)
    {
        float colorScale = SpawnAnalyzerUI.Visible ? 0.6f : 0.4f;

        Rectangle spawnAreaScreen = new(
            (int)(spawnArea.X * 16 - Main.screenPosition.X),
            (int)(spawnArea.Y * 16 - Main.screenPosition.Y),
            spawnArea.Width * 16, spawnArea.Height * 16
        );
        sb.DrawRectBorder(spawnAreaScreen, Color.Lime * colorScale, 2);
        ChatManager.DrawColorCodedStringWithShadow(sb, FontAssets.MouseText.Value, "Spawn area", spawnAreaScreen.TopLeft() + new Vector2(5), Color.White, 0f, Vector2.Zero, Vector2.One);

        Rectangle safeAreaScreen = new(
            (int)(safeArea.X * 16 - Main.screenPosition.X),
            (int)(safeArea.Y * 16 - Main.screenPosition.Y),
            safeArea.Width * 16, safeArea.Height * 16
        );
        sb.DrawRectBorder(safeAreaScreen, Color.Yellow * colorScale, 2);
        ChatManager.DrawColorCodedStringWithShadow(sb, FontAssets.MouseText.Value, "No spawn area", safeAreaScreen.TopLeft() + new Vector2(5), Color.White, 0f, Vector2.Zero, Vector2.One);

        Rectangle playerScreen = new(
            (int)(globalSpawner.pX * 16 - Main.screenPosition.X),
            (int)(globalSpawner.pY * 16 - Main.screenPosition.Y) - 16,
            2 * 16, 3 * 16
        );
        sb.DrawRectBorder(playerScreen, Color.Red * colorScale, 2);
        ChatManager.DrawColorCodedStringWithShadow(sb, FontAssets.MouseText.Value, "Player position", playerScreen.BottomLeft() + new Vector2(0, 5), Color.White, 0f, Vector2.Zero, Vector2.One);

        HashSet<Point> drawnSpots = new();

        IEnumerable<Point> allSpots = foundSpawnSpots.Keys.Concat(results.Keys);

        Point mouseWorldPos = Main.MouseWorld.ToPoint();
        mouseWorldPos.X /= 16;
        mouseWorldPos.Y /= 16;

        Point? drawUISelectedPos = null;
        Point? hoveredPos = null;

        foreach (Point pos in allSpots)
        {
            if (drawnSpots.Contains(pos))
                continue;
            drawnSpots.Add(pos);

            Rectangle rect = new(
                (int)(pos.X * 16 - Main.screenPosition.X),
                (int)(pos.Y * 16 - Main.screenPosition.Y),
                16, 16
            );

            bool spawnSpot = foundSpawnSpots.ContainsKey(pos);
            bool spawnResult = results.ContainsKey(pos);

            Color color;

            if (SpawnAnalyzerUI.Visible && SpawnAnalyzerUI.SelectedPos == pos)
            {
                drawUISelectedPos = pos;
                continue;
            }
            else if (spawnSpot && spawnResult)
                color = Color.Lerp(Color.Lime, Color.Yellow, 0.5f) * colorScale;
            else if (spawnSpot)
                color = Color.Lime * colorScale;
            else
                color = Color.Yellow * colorScale;

            bool hover = mouseWorldPos == pos;
            if (hover && SpawnAnalyzerUI.Visible)
            {
                hoveredPos = pos;
            }
            else
            {
                sb.DrawRectBorder(rect, color, 2);
            }

            if (hover && SpawnAnalyzerUI.Visible)
            {
                Main.LocalPlayer.mouseInterface = true;

                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    SpawnAnalyzerUI.SelectedPos = pos;
                    SoundEngine.PlaySound(SoundID.MenuTick);
                }
            }
        }

        if (drawUISelectedPos is not null)
        {
            Rectangle rect = new(
                (int)(drawUISelectedPos.Value.X * 16 - Main.screenPosition.X),
                (int)(drawUISelectedPos.Value.Y * 16 - Main.screenPosition.Y),
                16, 16
            );
            rect.Inflate(2, 2);
            sb.DrawRectBorder(rect, Color.Magenta * 0.9f, 2);
        }

        if (hoveredPos is not null)
        {
            if (lastHoveredSpot != hoveredPos)
                SoundEngine.PlaySound(SoundID.MenuTick);

            Rectangle rect = new(
                (int)(hoveredPos.Value.X * 16 - Main.screenPosition.X),
                (int)(hoveredPos.Value.Y * 16 - Main.screenPosition.Y),
                16, 16
            );
            rect.Inflate(4, 4);
            sb.DrawRectBorder(rect, Color.White * 0.9f, 2);
        }

        lastHoveredSpot = hoveredPos;
    }

    internal void MouseOver()
    {
        StringBuilder mouseOverText = new();
        if (foundSpawnSpots.TryGetValue((Main.MouseWorld / 16).ToPoint(), out var spawnSpot))
        {
            spawnSpot.MouseOver(mouseOverText);
        }

        if (results.TryGetValue((Main.MouseWorld / 16).ToPoint(), out var posDict))
        {
            if (posDict.Count == 1)
                mouseOverText.AppendLine("1 mob spawns here");
            else
                mouseOverText.AppendLine($"{posDict.Count} unique mobs spawns here");
        }

        if (mouseOverText.Length > 0)
        {
            string str = mouseOverText.ToString();
            Main.instance.MouseTextHackZoom(str, 0);
            Main.mouseText = true;
        }
    }
}