using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UISelectableTab : UITab, ISelectable
{

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            if (selected)
            {
                BackgroundColor = SelectedBackgroundColor;
                BorderColor = SelectedBorderColor;
            }
            else
            {
                BackgroundColor = DeselectedBackgroundColor;
                BorderColor = DeselectedBorderColor;
            }
        }
    }

    public Color SelectedBackgroundColor = new Color(63, 82, 151) * 0.7f;
    public Color HoverBackgroundColor = new Color(53, 72, 141) * 0.7f;
    public Color DeselectedBackgroundColor = new Color(43, 62, 131) * 0.7f;

    public Color SelectedBorderColor = Color.Black;
    public Color HoverBorderColor = Color.Black;
    public Color DeselectedBorderColor = Color.Black;

    private bool selected;
    private readonly Selection<UISelectableTab> selection;

    public object? Tag;

    public UISelectableTab(Selection<UISelectableTab> selection, string text)
        : base(text)
    {
        this.selection = selection;
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        if (!Selected)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            BackgroundColor = HoverBackgroundColor;
        }
    }

    public override void MouseOut(UIMouseEvent evt)
    {
        base.MouseOut(evt);
        if (!Selected)
        {
            BackgroundColor = DeselectedBackgroundColor;
        }
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        if (Selected)
            return;
            
        SoundEngine.PlaySound(SoundID.MenuTick);
        selection.CurrentSelection = this;
    }
}