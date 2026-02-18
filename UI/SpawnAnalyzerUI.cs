using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.LootSimulation.LootSimulatorConditionSetterTypes;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI;

public class SpawnAnalyzerUI : UIState
{
    static UserInterface ui = new();

    static SpawnAnalyzerUI? instance;

    public static bool Visible => ui.CurrentState is not null;

    static Vector2 MouseScreenRelative => Main.MouseScreen / Main.ScreenSize.ToVector2();

    Vector2 TopLeftRelative
    {
        get => new(Left.Precent, Top.Precent);
        set
        {
            Left.Precent = value.X;
            Top.Precent = value.Y;
        }
    }

    Vector2 SizeRelative
    {
        get => new(Width.Precent, Height.Precent);
        set
        {
            Width.Precent = value.X;
            Height.Precent = value.Y;
        }
    }

    Vector2 SizeAbsolute => new(Width.GetValue(Main.screenWidth), Height.GetValue(Main.screenHeight));
    Vector2 TopLeftAbsolute => new(Left.GetValue(Main.screenWidth), Top.GetValue(Main.screenHeight));

    Vector2? mainPanelGrabPos;
    bool grabbedResizer;

    const float MinWindowSize = 200f;
    const float ResizerSize = 20f;

    UIPanel mainPanel;

    public SpawnAnalyzerUI()
    {
        Top = new(0f, .35f);
        Left = new(0f, .35f);
        Width = new(MinWindowSize, .3f);
        Height = new(MinWindowSize, .3f);

        mainPanel = new()
        {
            Width = new(0, 1),
            Height = new(0, 1),
        };

        Append(mainPanel);
    }

    public static void Open()
    {
        if (Visible)
            return;

        instance ??= new();

        ui.SetState(instance);

        SoundEngine.PlaySound(SoundID.MenuOpen);
    }

    public static void Close()
    {
        if (!Visible)
            return;

        ui.SetState(null);

        SoundEngine.PlaySound(SoundID.MenuClose);
    }

    public static void Toggle()
    {
        if (Visible)
            Close();
        else
            Open();
    }

    public static void DrawLayer(SpriteBatch sb)
    {
        DrawOpenButton(sb);

        if (Visible && !Main.inFancyUI)
        {
            ui.Draw(sb, Main.gameTimeCache);
        }
    }

    public static void UpdateUI(GameTime gameTime)
    {
        if (Visible && !Main.inFancyUI)
        {
            ui.Update(gameTime);
        }
    }

    public static void DrawOpenButton(SpriteBatch sb)
    {
        if (Main.LocalPlayer.talkNPC != -1 || NewCraftingUI.Visible || Main.LocalPlayer.chest != -1 || !Main.playerInventory)
            return;

        Vector2 pos = new(45f, 283f);

        if (!Main.CreativeMenu.Blocked && Main.LocalPlayer.difficulty == 3)
        {
            pos.X += 36f;
        }

        Texture2D texture = SpawnAnalyzer.GetTexture("UIOpenButton");

        Point frameSize = new(texture.Width / 2, texture.Height);
        Rectangle hitbox = Terraria.Utils.CenteredRectangle(pos, frameSize.ToVector2());

        bool hover = hitbox.Contains(Main.MouseScreen.ToPoint());

        Rectangle frame = new(0, 0, frameSize.X, frameSize.Y);
        if (hover)
            frame.X += frame.Width;

        sb.Draw(texture, hitbox, frame, Color.White);

        if (hover)
        {
            Main.LocalPlayer.mouseInterface = true;
            string text = Visible ? "Close spawn analyzer" : "Open spawn analyzer";
            Main.instance.MouseTextNoOverride(text);

            if (Main.mouseLeft && Main.mouseLeftRelease)
            {
                Toggle();
            }
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (mainPanelGrabPos is null && mainPanel.IsMouseHovering && !PlayerInput.Triggers.Old.MouseLeft && PlayerInput.Triggers.Current.MouseLeft)
        {
            bool noChildrenHover = true;
            foreach (UIElement item in mainPanel.Children)
            {
                if (item.IsMouseHovering)
                {
                    noChildrenHover = false;
                    break;
                }
            }
            if (noChildrenHover)
            {
                Vector2 size = SizeAbsolute;
                Vector2 topLeft = TopLeftAbsolute;
                Vector2 mouseScreen = Main.MouseScreen;

                Vector2 center = topLeft + size / 2;
                Vector2 bottomRight = size + topLeft;
                grabbedResizer = false;

                if (mouseScreen.X > center.X && mouseScreen.Y > center.Y)
                {

                    if (bottomRight.X - mouseScreen.X <= ResizerSize
                     && bottomRight.Y - mouseScreen.Y <= ResizerSize
                    )
                    {
                        grabbedResizer = true;
                    }
                }

                if (grabbedResizer)
                    mainPanelGrabPos = bottomRight - mouseScreen;
                else
                    mainPanelGrabPos = mouseScreen - topLeft;
            }
        }

        if (mainPanelGrabPos is not null && !PlayerInput.Triggers.Current.MouseLeft)
        {
            mainPanelGrabPos = null;
        }

        if (mainPanelGrabPos is not null)
        {
            if (grabbedResizer)
            {
                Vector2 newSize = Main.MouseScreen + mainPanelGrabPos.Value - TopLeftAbsolute;

                Vector2 percents = (newSize - new Vector2(Width.Pixels, Height.Pixels)) / Main.ScreenSize.ToVector2();

                if (percents.X < 0)
                    percents.X = 0;
                if (percents.Y < 0)
                    percents.Y = 0;

                Width.Precent = percents.X;
                Height.Precent = percents.Y;
            }
            else
            {
                TopLeftRelative = (Main.MouseScreen - mainPanelGrabPos.Value) / Main.ScreenSize.ToVector2();
            }

        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (IsMouseHovering)
        {
            Main.LocalPlayer.mouseInterface = true;
        }
        base.Draw(spriteBatch);
    }
}