using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Chat;

namespace SpawnAnalyzer.UI;

public class UIButton: UIElement
{
    private readonly int cornerSize = 12;
	private readonly int barSize = 4;

	private readonly Asset<Texture2D> borderTexture;
	private readonly Asset<Texture2D> backgroundTexture;

    private List<PositionedSnippet> text;
    private Vector2 textSize;
    
	public Color BorderColor = Color.Black;
	public Color BackgroundColor = new Color(63, 82, 151) * 0.7f;

    public Color HoverBorderColor = Color.Black;
	public Color HoverBackgroundColor = new Color(73, 94, 171) * 0.7f;

    public Color ClickBorderColor = Color.Black;
	public Color ClickBackgroundColor = new Color(68, 88, 161) * 0.7f;

    public Vector2 TextAlign = new(0.5f);

	public UIButton(string text)
	{
		borderTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBorder");
		backgroundTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBackground");

        List<TextSnippet> snippets = ChatManager.ParseMessage(text, Color.White);
		ChatManager.ConvertNormalSnippets(snippets);
		this.text = ChatManager.LayoutSnippets(FontAssets.MouseText.Value, snippets, Vector2.One).ToList();
		textSize = ChatManager.GetStringSize(this.text);

        textSize.Y = Math.Max(0, textSize.Y - 8);
	}

	private void DrawPanel(SpriteBatch spriteBatch, Texture2D texture, Color color)
	{
		CalculatedStyle dimensions = GetDimensions();
		Point point = new((int)dimensions.X, (int)dimensions.Y);
		Point point2 = new(point.X + (int)dimensions.Width - cornerSize, point.Y + (int)dimensions.Height - cornerSize);
		int width = point2.X - point.X - cornerSize;
		int height = point2.Y - point.Y - cornerSize;
		spriteBatch.Draw(texture, new Rectangle(point.X, point.Y, cornerSize, cornerSize), new Rectangle(0, 0, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point2.X, point.Y, cornerSize, cornerSize), new Rectangle(cornerSize + barSize, 0, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point.X, point2.Y, cornerSize, cornerSize), new Rectangle(0, cornerSize + barSize, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point2.X, point2.Y, cornerSize, cornerSize), new Rectangle(cornerSize + barSize, cornerSize + barSize, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point.X + cornerSize, point.Y, width, cornerSize), new Rectangle(cornerSize, 0, barSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point.X + cornerSize, point2.Y, width, cornerSize), new Rectangle(cornerSize, cornerSize + barSize, barSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(point.X, point.Y + cornerSize, cornerSize, height), new Rectangle(0, cornerSize, cornerSize, barSize), color);
		spriteBatch.Draw(texture, new Rectangle(point2.X, point.Y + cornerSize, cornerSize, height), new Rectangle(cornerSize + barSize, cornerSize, cornerSize, barSize), color);
		spriteBatch.Draw(texture, new Rectangle(point.X + cornerSize, point.Y + cornerSize, width, height), new Rectangle(cornerSize, cornerSize, barSize, barSize), color);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
        Color borderColor;
        Color bgColor;

        if (IsMouseHovering)
        {
            if (Main.mouseLeft)
            {
                borderColor = ClickBorderColor;
                bgColor = ClickBackgroundColor;
            }
            else
            {
                borderColor = HoverBorderColor;
                bgColor = HoverBackgroundColor;
            }
        }
        else
        {
            borderColor = BorderColor;
            bgColor = BackgroundColor;
        }

		if (backgroundTexture != null)
			DrawPanel(spriteBatch, backgroundTexture.Value, bgColor);

        CalculatedStyle dims = GetInnerDimensions();
        Vector2 pos = (new Vector2(dims.Width, dims.Height) - textSize) * TextAlign + dims.Position();

        ChatManager.DrawColorCodedStringShadow(spriteBatch, FontAssets.MouseText.Value, text, pos, Color.Black, 0f, Vector2.Zero, Vector2.One);
        ChatManager.DrawColorCodedString(spriteBatch, FontAssets.MouseText.Value, text, pos, 0f, Vector2.Zero, Vector2.One, out _);

		if (borderTexture != null)
			DrawPanel(spriteBatch, borderTexture.Value, borderColor);
	}

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        SoundEngine.PlaySound(SoundID.MenuTick);
    }
}