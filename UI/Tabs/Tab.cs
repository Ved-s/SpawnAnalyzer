using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public abstract class Tab: UIElement
{
    Point? lastPos = null;

    public HashSet<UIElement> grabDragElements = new();

    public virtual Vector2 CalculateMinSize()
    {
        return Vector2.Zero;
    }

    public virtual void TabSelected(SpawnAnalyzerUI ui)
    {
        Point? newPos = SpawnAnalyzerUI.SelectedPos;
        if (newPos != lastPos)
        {
            lastPos = newPos;
            NewPosSelected(newPos);
        }
    }

    public virtual void NewPosSelected(Point? pos)
    {
        lastPos = pos;
    }
}