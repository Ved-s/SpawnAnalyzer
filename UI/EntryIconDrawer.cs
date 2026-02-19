using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Bestiary;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class EntryIconDrawer : UIElement
{
    IEntryIcon icon;

    public bool ForceHover = false;

    public EntryIconDrawer(IEntryIcon icon)
    {
        this.icon = icon;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle rectangle = GetDimensions().ToRectangle();
        icon.Draw(default, spriteBatch, new()
        {
            iconbox = rectangle,
            IsHovered = IsMouseHovering || ForceHover,
            IsPortrait = false,
        });
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        Rectangle rect = GetDimensions().ToRectangle();
        icon.Update(default, rect, new()
        {
            iconbox = rect,
            IsHovered = IsMouseHovering || ForceHover,
            IsPortrait = false,
        });
    }
}