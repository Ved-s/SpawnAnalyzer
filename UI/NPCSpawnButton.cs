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

    /// <summary>
    /// Treat chance as chance to spawn per tick and show average time for one mob to spawn
    /// </summary>
    public bool ShowChanceAsAverageTime;

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

        string chanceText;

        if (ShowChanceAsAverageTime)
        {
            double avgTicks = 1 / (double)chance;
            double avgSeconds = avgTicks / 60;

            chanceText = FormatTimeShort(avgSeconds);
        }
        else
        {
            chanceText = FormatPercentage(chance);
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

    public static string FormatTimeShort(double seconds)
    {
        // n.nnf
        // nn.nf
        // nnn...f

        if (seconds < 10) {
            return $"{seconds:0.00}s";
        }
        else if (seconds < 60) {
            return $"{seconds:0.0}s";
        }

        double minutes = seconds / 60;
        if (minutes < 10) {
            return $"{minutes:0.00}m";
        }
        else if (minutes < 60) {
            return $"{minutes:0.0}m";
        }

        double hours = seconds / 3600;
        if (hours < 10) {
            return $"{hours:0.00}h";
        }
        else if (hours < 24) {
            return $"{hours:0.0}h";
        }

        double days = seconds / 86400;
        if (days < 10) {
            return $"{days:0.00}d";
        }
        else if (days < 100) {
            return $"{days:0.0}d";
        }
        else if (days < 365.25) {
            return $"{(int)days:0}d";
        }

        double years = seconds / 31557600;
        if (years < 10) {
            return $"{years:0.00}y";
        }
        else if (years < 100) {
            return $"{years:0.0}y";
        }

        return $"{(int)years}y";
    }

    public static string FormatPercentage(float chance)
    {
        chance *= 100;

        if (chance >= 100)
        {
            return $"{(int)chance}%";
        }
        else if (chance >= 10)
        {
            return $"{chance:0.0}%";
        }
        else if (chance < 0.0001)
        {
            return $"~ 0%";
        }
        else if (chance < 0.01)
        {
            return $"<0.01%";
        }
        else
        {
            return $"{chance:0.00}%";
        }
    }
}
