using System;
using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIItemList : UIElement
{
    static Func<UIElement, CalculatedStyle> innerDimensionGetter = 
        ReflectionHelpers.GenerateInstanceFieldGetter<UIElement, CalculatedStyle>(Utils.GetFieldOrThrow<UIElement>("_innerDimensions"));

    static Action<UIElement, CalculatedStyle> innerDimensionSetter = 
        ReflectionHelpers.GenerateInstanceFieldSetter<UIElement, CalculatedStyle>(Utils.GetFieldOrThrow<UIElement>("_innerDimensions"));


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

        CalculatedStyle? resetDims = null;
        if (AutoHeight)
        {
            resetDims = innerDimensionGetter(this);
            CalculatedStyle newDims = resetDims.Value;
            newDims.Height = float.MaxValue;
            innerDimensionSetter(this, newDims);
        }

        base.RecalculateChildren();

        if (resetDims is not null)
            innerDimensionSetter(this, resetDims.Value);


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
            maxHeight = Math.Max(maxHeight, y + itemDims.Height);
        }

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