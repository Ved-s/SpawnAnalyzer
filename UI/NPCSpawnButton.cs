using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpawnAnalyzer.Simulation;
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
    public readonly AnalyzedMultiSpawn spawn;

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
        set
        {
            selected = value;
            panel.BackgroundColor = selected ? hoverColor : normalColor;
            panel.BorderColor = selected ? Color.White : Color.Black;
        }
    }

    public NPCSpawnButton(AnalyzedMultiSpawn spawn, Selection<NPCSpawnButton> selection)
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

        icon = new(new UnlockableNPCEntryIcon(spawn.id))
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

        float chance = 0;
        bool affectedByLuck = false;

        foreach (var spawn in spawn.spawns)
        {
            if (spawn.chance > chance)
                chance = spawn.chance;

            if (spawn.affectedByLuck)
                affectedByLuck = true;
        }

        chance *= 100;

        string chanceText;

        if (chance >= 100)
        {
            chanceText = $"{(int)chance}%";
        }
        else if (chance >= 10)
        {
            chanceText = $"{chance:0.0}%";
        }
        else if (chance < 0.0001)
        {
            chanceText = $"~ 0%";
        }
        else if (chance < 0.01)
        {
            chanceText = $"<0.01%";
        }
        else
        {
            chanceText = $"{chance:0.00}%";
        }

        Vector2 chanceTextPos = dims.BottomRight() - new Vector2(5, -7) - FontAssets.MouseText.Value.MeasureString(chanceText);

        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, chanceText, chanceTextPos, Color.White, 0f, Vector2.One, Vector2.One);

        if (spawn.spawns.Count > 1)
        {
            string extraText = $"+{spawn.spawns.Count - 1}";
            Vector2 extraTextPos = dims.BottomRight() - new Vector2(5, 13) - FontAssets.MouseText.Value.MeasureString(extraText);
            ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, extraText, extraTextPos, Color.White, 0f, Vector2.One, Vector2.One);
        }

        if (affectedByLuck)
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
