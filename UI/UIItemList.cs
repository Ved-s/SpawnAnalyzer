using System;
using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIItemList : UIElement
{
    public Vector2 ItemSpacing = new(10);

    public int? FixedItemsPerRow = null;

    bool autoHeight = false;
    bool recalcSelfOnly = false;

    public bool AutoHeight
    {
        get => autoHeight;
        set
        {
            autoHeight = value;
            OverflowHidden = !autoHeight;
        }
    }

    public UIItemList()
    {
        OverflowHidden = !AutoHeight;
    }

    public override void RecalculateChildren()
    {
        if (recalcSelfOnly)
        {
            recalcSelfOnly = false;
            return;
        }
        base.RecalculateChildren();

        CalculatedStyle dims = GetInnerDimensions();

        float x = 0;
        float y = 0;
        float maxRowHeight = 0;

        float maxHeight = 0;

        int rowItem = 0;

        foreach (var item in Children)
        {
            CalculatedStyle itemDims = item.GetOuterDimensions();

            bool newline;
            if (FixedItemsPerRow is null)
            {
                newline = rowItem > 0 && x + itemDims.Width > dims.Width;
            }
            else
            {
                newline = rowItem >= FixedItemsPerRow.Value;
            }

            if (newline)
            {
                x = 0;

                maxHeight = y + maxRowHeight;

                y += maxRowHeight + ItemSpacing.Y;
                maxRowHeight = 0;
                rowItem = 0;

            }

            item.Left = new(x, 0);
            item.Top = new(y, 0);
            rowItem++;

            x += itemDims.Width + ItemSpacing.X;

            maxRowHeight = Math.Max(maxRowHeight, itemDims.Height);
        }

        maxHeight = Math.Max(maxHeight, y + maxRowHeight);

        if (AutoHeight)
        {
            var oldHeight = Height;
            Height = new(maxHeight, 0);

            if (oldHeight.Precent != Height.Precent || Math.Abs(oldHeight.Pixels - Height.Pixels) > 0.1f)
            {
                recalcSelfOnly = true;
                Recalculate();
            }
        }
    }
}