using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Chat;

namespace SpawnAnalyzer.UI;

public class NPCSpawnButton : UIElement, ISelectable
{
    public readonly SpawnAnalysisResultSpawn spawn;

    readonly UIEntityIcon icon;
    readonly UIPanel panel;

    readonly Selection<NPCSpawnButton> selection;

    public const float FixedWidth = 110;

    static Color normalColor = new Color(63, 82, 151) * 0.7f;
    static Color hoverColor = new Color(83, 102, 171) * 0.7f;
    private bool selected;

    public bool Selected
    {
        get => selected;
        set {
            selected = value;
            panel.BackgroundColor = selected ? hoverColor : normalColor;
            panel.BorderColor = selected ? Color.White : Color.Black;
        }
    }

    public NPCSpawnButton(SpawnAnalysisResultSpawn spawn, Selection<NPCSpawnButton> selection)
    {
        this.spawn = spawn;
        this.selection = selection;

        Height.Set(72f, 0f);
        Width.Set(FixedWidth, 0f);

        panel = new()
        {
            Width = new(0, 1),
            Height = new(0, 1),
            OverflowHidden = true,
        };
        panel.SetPadding(2);

        icon = new(new UnlockableNPCEntryIcon(spawn.npcId))
        {
            Width = new(68f, 0),
            Height = new(68f, 0),
        };
        panel.Append(icon);

        Append(panel);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        Rectangle dims = GetDimensions().ToRectangle();

        float chance = spawn.chance * 100;

        string text;

        if (chance >= 100)
        {
            text = $"{(int)chance}%";
        }
        else if (chance >= 10)
        {
            text = $"{chance:0.0}%";
        }
        else if (chance < 0.01)
        {
            text = $"<0.01%";
        }
        else
        {
            text = $"{chance:0.00}%";
        }

        Vector2 textPos = dims.BottomRight() - new Vector2(5, -7) - FontAssets.MouseText.Value.MeasureString(text);

        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, text, textPos, Color.White, 0f, Vector2.One, Vector2.One);

        if (spawn.dependsOnLuck)
        {
            Texture2D luck = SpawnAnalyzer.GetTexture("Luck");

            Rectangle luckRect = new(dims.Right - luck.Width, dims.Top, luck.Width, luck.Height);

            spriteBatch.Draw(luck, luckRect, Color.White);
        }
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        icon.ForceHover = true;
        SoundEngine.PlaySound(SoundID.MenuTick);

        if (!Selected)
            panel.BackgroundColor = hoverColor;
    }

    public override void MouseOut(UIMouseEvent evt)
    {
        base.MouseOut(evt);
        icon.ForceHover = false;

        if (!Selected)
            panel.BackgroundColor = normalColor;
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        selection.CurrentSelection = this;
        SoundEngine.PlaySound(SoundID.MenuTick);
    }
}
