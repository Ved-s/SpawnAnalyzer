using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Bestiary;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIEntityIcon : UIElement
{
    IEntryIcon icon;

    public bool ForceHover = false;

    bool updated = false;

    public UIEntityIcon(IEntryIcon icon)
    {
        this.icon = icon;
    }

    Rectangle GetIconRect()
    {
        var dims = GetDimensions();

        return new Rectangle((int)Math.Round(dims.X), (int)Math.Round(dims.Y), (int)Math.Round(dims.Width), (int)Math.Round(dims.Height));
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle rect = GetIconRect();
        if (!updated)
        {
            icon.Update(default, rect, new()
            {
                iconbox = rect,
                IsHovered = IsMouseHovering || ForceHover,
                IsPortrait = false,
            });
        }

        icon.Draw(default, spriteBatch, new()
        {
            iconbox = rect,
            IsHovered = IsMouseHovering || ForceHover,
            IsPortrait = false,
        });
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        Rectangle rect = GetIconRect();
        icon.Update(default, rect, new()
        {
            iconbox = rect,
            IsHovered = IsMouseHovering || ForceHover,
            IsPortrait = false,
        });
        updated = true;
    }
}