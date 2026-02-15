using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace SpawnAnalyzer;

class SpawnAnalysis
{
    Rectangle SpawnArea;
    Rectangle SafeArea;

    // (tileParams, chance @ 0..=1)
    readonly Dictionary<Point, SpawnAnalysisSpot> FoundSpawnSpots = new();

    readonly NPC.Spawner GlobalSpawner = new();

    public SpawnAnalysis(Player player)
    {
        NPC.Spawner.GetSpawnArea(player, out SpawnArea, out SafeArea);

        Utils.GetMethodOrThrow<NPC.Spawner>("SetSpawnFlags").Invoke(GlobalSpawner, [player]);

        // Seems like GetSpawnTileParams only outputs one type of spawn params per tile, pick the first
        // and show warnings on multiple different

        bool warning = false;

        for (int y = SpawnArea.Top; y < SpawnArea.Bottom; y++)
        {
            for (int x = SpawnArea.Left; x < SpawnArea.Right; x++)
            {
                int xRef = x;
                int yRef = y;
                if (SpawnAnalyzer.GetSpawnTileParamsImpl(GlobalSpawner, player, ref xRef, ref yRef, SpawnArea, SafeArea, out SpawnParamsStage1 spawnParams))
                {

                    if (!FoundSpawnSpots.TryGetValue(new(xRef, yRef), out SpawnAnalysisSpot? spot))
                    {
                        FoundSpawnSpots.Add(new(xRef, yRef), new SpawnAnalysisSpot(GlobalSpawner, new(xRef, yRef), spawnParams));
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
    }

    public void DrawOverlay(SpriteBatch sb)
    {
        Rectangle spawnAreaScreen = new(
            (int)(SpawnArea.X * 16 - Main.screenPosition.X),
            (int)(SpawnArea.Y * 16 - Main.screenPosition.Y),
            SpawnArea.Width * 16, SpawnArea.Height * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, spawnAreaScreen, Color.Green * 0.1f);

        Rectangle safeAreaScreen = new(
            (int)(SafeArea.X * 16 - Main.screenPosition.X),
            (int)(SafeArea.Y * 16 - Main.screenPosition.Y),
            SafeArea.Width * 16, SafeArea.Height * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, safeAreaScreen, Color.Yellow * 0.1f);

        Rectangle playerScreen = new(
            (int)(GlobalSpawner.pX * 16 - Main.screenPosition.X),
            (int)(GlobalSpawner.pY * 16 - Main.screenPosition.Y),
            2 * 16, 3 * 16
        );
        sb.Draw(TextureAssets.MagicPixel.Value, playerScreen, Color.Red * 0.2f);

        foreach (KeyValuePair<Point, SpawnAnalysisSpot> kvp in FoundSpawnSpots)
        {
            Rectangle rect = new(
                (int)(kvp.Key.X * 16 - Main.screenPosition.X),
                (int)(kvp.Key.Y * 16 - Main.screenPosition.Y),
                16, 16
            );
            sb.Draw(TextureAssets.MagicPixel.Value, rect, Color.Lime * 0.7f);
        }
    }

    internal void MouseOver()
    {
        StringBuilder mouseOverText = new();
        if (FoundSpawnSpots.TryGetValue((Main.MouseWorld / 16).ToPoint(), out var spawnSpot))
        {
            spawnSpot.MouseOver(mouseOverText);
        }

        if (mouseOverText.Length > 0)
        {
            Main.instance.MouseTextHackZoom(mouseOverText.ToString(), 0);
            Main.mouseText = true;
        }
    }
}