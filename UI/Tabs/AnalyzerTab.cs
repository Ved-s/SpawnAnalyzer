using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public class AnalyzerTab : Tab
{
    UIElement centerElement;

    UIButton analyzeHereButton;
    UIButton clearAnalysisButton;
    UIButton reAnalyzeButton;

    UIText nonDeterministicText;

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

        nonDeterministicText = new("Warning! Simulated code contains randomness or side-effects.\nSome spawns may not appear in the list or have wrong values.")
        {
            Height = new(50, 0),
            TextColor = Color.Orange
        };
        nonDeterministicText.Width = nonDeterministicText.MinWidth;

        grabDragElements.Add(centerElement);
        UpdateCenterElement();
    }

    void UpdateCenterElement()
    {
        centerElement.RemoveAllChildren();

        float width = 0;

        float y = 0;

        analyzeHereButton.Top.Pixels = y;
        y += analyzeHereButton.Height.Pixels;

        centerElement.Append(analyzeHereButton);
        width = Math.Max(width, analyzeHereButton.Width.Pixels);

        if (SpawnAnalyzer.LastAnalysis is not null)
        {
            y += 10;
            clearAnalysisButton.Top.Pixels = y;
            reAnalyzeButton.Top.Pixels = y;

            centerElement.Append(clearAnalysisButton);
            centerElement.Append(reAnalyzeButton);

            y += Math.Max(clearAnalysisButton.Height.Pixels, reAnalyzeButton.Height.Pixels);
            width = Math.Max(width, clearAnalysisButton.Width.Pixels + 10 + reAnalyzeButton.Width.Pixels);
        }

        if (SpawnAnalyzer.DefaultImpl?.SpawnAnNpcRewrite.PossiblyNonDeterministic is true) {
            y += 10;
            nonDeterministicText.Top.Pixels = y;

            centerElement.Append(nonDeterministicText);

            y += nonDeterministicText.Height.Pixels;
            width = Math.Max(width, nonDeterministicText.Width.Pixels);
        }

        centerElement.Width.Pixels = width;
        centerElement.Height.Pixels = y;

        centerElement.Left.Pixels = -width / 2;
        centerElement.Top.Pixels = -y / 2;

        Recalculate();

        if (Parent is not null)
            SpawnAnalyzerUI.Instance?.UpdateMinSize();
    }

    public override Vector2 CalculateMinSize()
    {
        return new(centerElement.Width.Pixels, centerElement.Height.Pixels);
    }
}