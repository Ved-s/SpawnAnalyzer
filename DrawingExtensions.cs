using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

public static class DrawingExtensions
{
    public static void DrawRectBorder(this SpriteBatch sb, Rectangle rect, Color color, int width = 1)
    {
        Rectangle top = new(rect.X + width, rect.Y, rect.Width - width, width);
        Rectangle left = new(rect.X, rect.Y, width, rect.Height - width);
        Rectangle right = new(rect.X + rect.Width - width, rect.Y + width, width, rect.Height - width);
        Rectangle bottom = new(rect.X, rect.Y + rect.Height - width, rect.Width - width, width);

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        sb.Draw(pixel, top, color);
        sb.Draw(pixel, left, color);
        sb.Draw(pixel, right, color);
        sb.Draw(pixel, bottom, color);
    }
}