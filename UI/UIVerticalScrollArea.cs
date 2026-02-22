using System;
using System.Reflection;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIVerticalScrollArea: UIElement
{
    static Func<UIElement, CalculatedStyle> innerDimensionGetter = 
        ReflectionHelpers.GenerateInstanceFieldGetter<UIElement, CalculatedStyle>(Utils.GetFieldOrThrow<UIElement>("_innerDimensions"));

    static Action<UIElement, CalculatedStyle> innerDimensionSetter = 
        ReflectionHelpers.GenerateInstanceFieldSetter<UIElement, CalculatedStyle>(Utils.GetFieldOrThrow<UIElement>("_innerDimensions"));

    private UIElement scrollingElement;

    public UIElement ScrollingElement { 
        get => scrollingElement;
        set
        {
            RemoveChild(scrollingElement);
            scrollingElement = value;
            Append(scrollingElement);

            scrollingElement.Width = new(-scrollBar.Width.Pixels, 1);
            scrollingElement.Left = new(0, 0);
        }
    }

    UIScrollbar scrollBar;

    public UIVerticalScrollArea(UIElement scrollingElement)
    {
        this.scrollingElement = scrollingElement;
        scrollBar = new();

        scrollBar.Left = new(-scrollBar.Width.Pixels, 1);
        scrollBar.Top = new(6, 0);
        scrollBar.Height = new(-12, 1);

        scrollingElement.Width = new(-scrollBar.Width.Pixels, 1);
        scrollingElement.Left = new(0, 0);

        Append(scrollingElement);
        Append(scrollBar);

        OverflowHidden = true;
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        scrollBar.ViewPosition -= evt.ScrollWheelValue;
        RecalculateChildren();
    }

    public override void RecalculateChildren()
    {
        scrollingElement.Top = new(-scrollBar.ViewPosition, 0);

        CalculatedStyle style = innerDimensionGetter(this);

        CalculatedStyle styleInfHeight = style;
        styleInfHeight.Height = float.MaxValue;
        innerDimensionSetter(this, styleInfHeight);

        scrollingElement.Recalculate();

        innerDimensionSetter(this, style);

        scrollBar.Recalculate();

        scrollBar.SetView(GetInnerDimensions().Height, scrollingElement.GetOuterDimensions().Height);
    }
}