using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Chat;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria.UI.Chat;

namespace SpawnAnalyzer.UI;

public class UITab : UIElement
{
    private readonly int cornerSize = 12;
    private readonly int barSize = 4;
    private readonly Asset<Texture2D> borderTexture;
    private readonly Asset<Texture2D> backgroundTexture;
    public Color BorderColor = Color.Black;
    public Color BackgroundColor = new Color(63, 82, 151) * 0.7f;
    public string Text;

    public UITab(string text)
    {
        Text = text;
        borderTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBorder");
        backgroundTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBackground");
    }

    private void DrawTab(SpriteBatch spriteBatch, Texture2D texture, Color color)
    {
        CalculatedStyle dimensions = GetDimensions();
        Point outerTopLeft = new((int)dimensions.X, (int)dimensions.Y);
        Point innerBottomRight = new(outerTopLeft.X + (int)dimensions.Width - cornerSize, outerTopLeft.Y + (int)dimensions.Height);
        int innerWidth = innerBottomRight.X - outerTopLeft.X - cornerSize;
        int innerHeight = innerBottomRight.Y - outerTopLeft.Y - cornerSize;

        // TL corner
        spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X, outerTopLeft.Y, cornerSize, cornerSize), new Rectangle(0, 0, cornerSize, cornerSize), color);

        // TR corner
        spriteBatch.Draw(texture, new Rectangle(innerBottomRight.X, outerTopLeft.Y, cornerSize, cornerSize), new Rectangle(cornerSize + barSize, 0, cornerSize, cornerSize), color);

        // top edge
        spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X + cornerSize, outerTopLeft.Y, innerWidth, cornerSize), new Rectangle(cornerSize, 0, barSize, cornerSize), color);


        // left edge
        spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X, outerTopLeft.Y + cornerSize, cornerSize, innerHeight), new Rectangle(0, cornerSize, cornerSize, barSize), color);

        // right edge
        spriteBatch.Draw(texture, new Rectangle(innerBottomRight.X, outerTopLeft.Y + cornerSize, cornerSize, innerHeight), new Rectangle(cornerSize + barSize, cornerSize, cornerSize, barSize), color);


        // center
        spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X + cornerSize, outerTopLeft.Y + cornerSize, innerWidth, innerHeight), new Rectangle(cornerSize, cornerSize, barSize, barSize), color);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (backgroundTexture is not null)
            DrawTab(spriteBatch, backgroundTexture.Value, BackgroundColor);

        if (borderTexture is not null)
            DrawTab(spriteBatch, borderTexture.Value, BorderColor);

        Vector2 textSize = FontAssets.MouseText.Value.MeasureString(Text);
        Rectangle textRect = GetDimensions().ToRectangle();
        textRect.Y += 2;
        textRect.Height -= 2;

        Vector2 textPos = textRect.Center() - (textSize / 2) + new Vector2(0, 4);

        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, Text, textPos, Color.White, 0, Vector2.Zero, Vector2.One);
    }
}
