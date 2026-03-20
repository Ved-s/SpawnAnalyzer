using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public class AnalyzerTab : Tab
{
    UIElement centerElement;

    UIButton analyzeHereButton;
    UIButton clearAnalysisButton;

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
            Left = new(-75, 0.5f),
            Height = new(40, 0),
            Width = new(150, 0),
        };
        analyzeHereButton.OnLeftClick += (_, _) =>
        {
            SpawnAnalyzer.BeginAnalyze(Main.player[Main.myPlayer]);
            UpdateCenterElement();
        };

        clearAnalysisButton = new("Clear")
        {
            Left = new(-50, 0.5f),
            Top = new(50, 0),
            Height = new(30, 0),
            Width = new(100, 0),
        };
        clearAnalysisButton.OnLeftClick += (_, _) =>
        {
            SpawnAnalyzer.ClearAnalysis();
            UpdateCenterElement();
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
            height += clearAnalysisButton.Height.Pixels;
            width = Math.Max(width, clearAnalysisButton.Width.Pixels);
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