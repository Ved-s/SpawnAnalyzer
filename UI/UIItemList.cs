using System;
using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIItemList: UIElement
{
    public Vector2 ItemSpacing = new(10);

    public UIItemList()
    {
        OverflowHidden = true;
    }

    public override void RecalculateChildren()
    {
        base.RecalculateChildren();

        CalculatedStyle dims = GetInnerDimensions();

        float x = 0;
        float y = 0;
        float maxRowHeight = 0;

        foreach (var item in Children)
        {
            CalculatedStyle itemDims = item.GetOuterDimensions();

            if (x + itemDims.Width > dims.Width && x > 0)
            {
                x = 0;
                y += maxRowHeight + ItemSpacing.Y;
                maxRowHeight = 0;
            }

            item.Left = new(x, 0);
            item.Top = new(y, 0);

            x += itemDims.Width + ItemSpacing.X;
            maxRowHeight = Math.Max(maxRowHeight, itemDims.Height);
        }
    }
}