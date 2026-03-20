using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public class AnalyzerTab : Tab
{
    UIElement centerElement;

    UIButton analyzeHereButton;
    UIButton clearAnalysisButton;
    UIButton reAnalyzeButton;

    public AnalyzerTab()
    {
        centerElement = new()
        {
            Top = new(0, 0.5f),
            Left = new(0, 0.5f),
        };
        Append(centerElement);

        analyzeHereButton = new("Analyze here")
        {
            Left = new(-115, 0.5f),
            Height = new(40, 0),
            Width = new(230, 0),
        };
        analyzeHereButton.OnLeftClick += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            SpawnAnalyzer.BeginAnalyze(Main.player[Main.myPlayer]);
            UpdateCenterElement();
        };

        clearAnalysisButton = new("Clear")
        {
            Left = new(-110 - 5, 0.5f),
            Top = new(50, 0),
            Height = new(30, 0),
            Width = new(110, 0),
        };
        clearAnalysisButton.OnLeftClick += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            SpawnAnalyzer.ClearAnalysis();
            UpdateCenterElement();
        };

        reAnalyzeButton = new("Re-analyze")
        {
            Left = new(5, 0.5f),
            Top = new(50, 0),
            Height = new(30, 0),
            Width = new(110, 0),
        };
        reAnalyzeButton.OnLeftClick += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            SpawnAnalyzer.LastAnalysis?.Simulate();
            SpawnAnalyzerUI.SendEvent(new NewPosSelectedEvent(SpawnAnalyzerUI.SelectedPos));
        };

        grabDragElements.Add(centerElement);
        UpdateCenterElement();
    }

    void UpdateCenterElement()
    {
        centerElement.RemoveAllChildren();

        float height = 0;
        float width = 0;

        centerElement.Append(analyzeHereButton);
        height += analyzeHereButton.Height.Pixels;
        width = Math.Max(width, analyzeHereButton.Width.Pixels);

        if (SpawnAnalyzer.LastAnalysis is not null)
        {
            height += 10;

            centerElement.Append(clearAnalysisButton);
            centerElement.Append(reAnalyzeButton);

            height += Math.Max(clearAnalysisButton.Height.Pixels, reAnalyzeButton.Height.Pixels);
            width = Math.Max(width, clearAnalysisButton.Width.Pixels + 10 + reAnalyzeButton.Width.Pixels);
        }

        centerElement.Width.Pixels = width;
        centerElement.Height.Pixels = height;

        centerElement.Left.Pixels = -width/2;
        centerElement.Top.Pixels = -height/2;

        Recalculate();

        if (Parent is not null)
            SpawnAnalyzerUI.Instance?.UpdateMinSize();
    }

    public override Vector2 CalculateMinSize()
    {
        return new(centerElement.Width.Pixels, centerElement.Height.Pixels);
    }
}