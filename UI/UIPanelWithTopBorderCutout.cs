using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class UIPanelWithTopBorderCutout : UIElement
{
	private readonly int cornerSize = 12;
	private readonly int barSize = 4;
	private readonly Asset<Texture2D> borderTexture;
	private readonly Asset<Texture2D> backgroundTexture;

	public Color BorderColor = Color.Black;
	public Color BackgroundColor = new Color(63, 82, 151) * 0.7f;

	public delegate bool CalculateCutoutDelegate(out int cutoutXStart, out int cutoutWidth);
	public CalculateCutoutDelegate? CalculateCutout;

	public UIPanelWithTopBorderCutout()
	{
		borderTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBorder");

		backgroundTexture ??= Main.Assets.Request<Texture2D>("Images/UI/PanelBackground");

		SetPadding(cornerSize);
	}

	private void DrawPanel(SpriteBatch spriteBatch, Texture2D texture, Color color)
	{
		CalculatedStyle dimensions = GetDimensions();
		Point outerTopLeft = new((int)dimensions.X, (int)dimensions.Y);
		Point innerBottomRight = new(outerTopLeft.X + (int)dimensions.Width - cornerSize, outerTopLeft.Y + (int)dimensions.Height - cornerSize);
		int innerWidth = innerBottomRight.X - outerTopLeft.X - cornerSize;
		int innerHeight = innerBottomRight.Y - outerTopLeft.Y - cornerSize;

		spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X, outerTopLeft.Y, cornerSize, cornerSize), new Rectangle(0, 0, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(innerBottomRight.X, outerTopLeft.Y, cornerSize, cornerSize), new Rectangle(cornerSize + barSize, 0, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X, innerBottomRight.Y, cornerSize, cornerSize), new Rectangle(0, cornerSize + barSize, cornerSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(innerBottomRight.X, innerBottomRight.Y, cornerSize, cornerSize), new Rectangle(cornerSize + barSize, cornerSize + barSize, cornerSize, cornerSize), color);

		if (CalculateCutout is null || !CalculateCutout(out int cutoutStart, out int cutoutWidth))
			spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X + cornerSize, outerTopLeft.Y, innerWidth, cornerSize), new Rectangle(cornerSize, 0, barSize, cornerSize), color);
		else
		{
			int topBarStartX = outerTopLeft.X + cornerSize;
			int topBarEndX = topBarStartX + innerWidth;
			int cutoutEnd = cutoutStart + cutoutWidth;

			if (cutoutStart >= topBarEndX || cutoutEnd <= topBarStartX)
			{
				spriteBatch.Draw(texture, new Rectangle(topBarStartX, outerTopLeft.Y, innerWidth, cornerSize), new Rectangle(cornerSize, 0, barSize, cornerSize), color);
			}
			else
			{
				if (cutoutStart > topBarStartX)
				{
					int width = cutoutStart - topBarStartX;
					spriteBatch.Draw(texture, new Rectangle(topBarStartX, outerTopLeft.Y, width, cornerSize), new Rectangle(cornerSize, 0, barSize / 2, cornerSize), color);
				}

				if (cutoutEnd < topBarEndX)
				{
					int width = topBarEndX - cutoutEnd;
					spriteBatch.Draw(texture, new Rectangle(cutoutEnd, outerTopLeft.Y, width, cornerSize), new Rectangle(cornerSize + barSize / 2, 0, barSize / 2, cornerSize), color);
				}

				spriteBatch.Draw(texture, new Rectangle(cutoutStart, outerTopLeft.Y, cutoutWidth, cornerSize), new Rectangle(cornerSize, cornerSize, barSize, barSize), color);
			}

		}

		spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X + cornerSize, innerBottomRight.Y, innerWidth, cornerSize), new Rectangle(cornerSize, cornerSize + barSize, barSize, cornerSize), color);
		spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X, outerTopLeft.Y + cornerSize, cornerSize, innerHeight), new Rectangle(0, cornerSize, cornerSize, barSize), color);
		spriteBatch.Draw(texture, new Rectangle(innerBottomRight.X, outerTopLeft.Y + cornerSize, cornerSize, innerHeight), new Rectangle(cornerSize + barSize, cornerSize, cornerSize, barSize), color);
		spriteBatch.Draw(texture, new Rectangle(outerTopLeft.X + cornerSize, outerTopLeft.Y + cornerSize, innerWidth, innerHeight), new Rectangle(cornerSize, cornerSize, barSize, barSize), color);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		if (backgroundTexture != null)
			DrawPanel(spriteBatch, backgroundTexture.Value, BackgroundColor);

		if (borderTexture != null)
			DrawPanel(spriteBatch, borderTexture.Value, BorderColor);
	}
}