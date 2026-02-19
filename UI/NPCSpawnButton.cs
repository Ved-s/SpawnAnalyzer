using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria.UI.Chat;

namespace SpawnAnalyzer.UI;

public class NPCSpawnButton : UIElement
{
    public readonly SpawnAnalysisResultSpawn spawn;

    EntryIconDrawer icon;

    public NPCSpawnButton(SpawnAnalysisResultSpawn spawn)
    {
        this.spawn = spawn;
        Height.Set(72f, 0f);
        Width.Set(110f, 0f);

        UIPanel panel = new()
        {
            Width = new(0, 1),
            Height = new(0, 1),
        };
        panel.SetPadding(4);

        icon = new(new UnlockableNPCEntryIcon(spawn.npcId))
        {
            Width = new(72f, 0),
            Height = new(72f, 0),
        };
        panel.Append(icon);

        Append(panel);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        CalculatedStyle dims = GetDimensions();

        string text = $"{spawn.chance * 100:0.0}%";

        Vector2 textPos = dims.ToRectangle().BottomRight() - new Vector2(5, -7) - FontAssets.MouseText.Value.MeasureString(text);

        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, text, textPos, Color.White, 0f, Vector2.One, Vector2.One);
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        icon.ForceHover = true;
    }

    public override void MouseOut(UIMouseEvent evt)
    {
        base.MouseOut(evt);
        icon.ForceHover = false;
    }
}
