using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public abstract class Tab : UIElement
{
    Point? lastPos = null;
    SpawnAnalysis? lastAnalysis = null;

    public HashSet<UIElement> grabDragElements = new();

    public virtual Vector2 CalculateMinSize()
    {
        return Vector2.Zero;
    }

    public virtual void TabSelected(SpawnAnalyzerUI ui)
    {
        if (!ReferenceEquals(lastAnalysis, SpawnAnalyzer.LastAnalysis))
        {
            lastAnalysis = SpawnAnalyzer.LastAnalysis;
            OnEvent(new NewSpawnAnalysisEvent(lastAnalysis));
        }

        Point? newPos = SpawnAnalyzerUI.SelectedPos;
        if (newPos != lastPos)
        {
            lastPos = newPos;
            OnEvent(new NewPosSelectedEvent(newPos));
        }
    }

    public virtual void OnEvent(SpawnAnalyzerUIEvent ev)
    {
        switch (ev)
        {
            case NewPosSelectedEvent np:
                lastPos = np.Position;
                break;

            case NewSpawnAnalysisEvent na:
                lastAnalysis = na.Analysis;
                break;
        }
    }
}