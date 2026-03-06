using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpawnAnalyzer.UI.Tabs;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.Generation.Dungeon.Halls;
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

    static Point? lastSelectedPos;
    static Point? selectedPos;

    static bool uiToggleButtonHovered;

    static public Point? SelectedPos
    {
        get => selectedPos;
        set
        {
            selectedPos = value;
            if (Visible && lastSelectedPos != selectedPos)
            {
                lastSelectedPos = selectedPos;
                instance!.NewPosSelected(selectedPos);
            }
        }
    }

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

    UIPanelWithTopBorderCutout mainPanel;

    Tab? currentTab;

    List<UISelectableTab> tabs = new();
    UIElement tabsUi;
    Selection<UISelectableTab> tabSelection = new();

    HashSet<UIElement> grabDragElements = new();

    Texture2D grabber = SpawnAnalyzer.GetTexture("UIGrabber");

    const float TabSpacing = 4;

    public SpawnAnalyzerUI()
    {
        Top = new(0f, .35f);
        Left = new(0f, .35f);
        Width = new(12 * 2, .3f);
        Height = new(200, .3f);

        tabsUi = new()
        {
            Width = new(-(70 + 10 + 12), 1),
            Height = new(32, 0),
            Left = new(12, 0),
        };

        tabSelection.OnSelectionChanged += (t) =>
        {
            SelectTab(t?.Tag as Tab);  
        };

        UIButton closeButton = new("Close")
        {
            Width = new(70, 0),
            Height = new(28, 0),
            Top = new(2, 0),
            Left = new(-70, 1),
        };
        closeButton.OnLeftClick += (_, _) => Close();

        Append(tabsUi);
        Append(closeButton);

        mainPanel = new()
        {
            Width = new(0, 1),
            Height = new(-32, 1),
            Top = new(32, 0),
            CalculateCutout = (out int start, out int length) =>
            {
                UIElement? tab = tabSelection.CurrentSelection;
                start = 0;
                length = 0;
                if (tab is null)
                    return false;

                CalculatedStyle dims = tab.GetDimensions();

                start = (int)dims.X;
                length = (int)dims.Width;
                return true;
            }
        };
        mainPanel.SetPadding(0);

        grabDragElements.Add(mainPanel);

        Append(mainPanel);


        tabs.Add(new(tabSelection, "Final spawns")
        {
            Tag = new FinalSpawnsTab(),

            Width = new(140, 0),
            Height = new(32, 0),
        });

        tabs.Add(new(tabSelection, "Analyzer")
        {
            Tag = new AnalyzerTab(),

            Width = new(140, 0),
            Height = new(32, 0),
        });

        UpdateTabListUI();

        if (tabs.Count > 0)
        {
            tabSelection.CurrentSelection = tabs[0];
        }
    }

    public static void Open()
    {
        if (Visible)
            return;

        try
        {
            instance ??= new();

            ui.SetState(instance);

            if (lastSelectedPos != selectedPos)
            {
                lastSelectedPos = selectedPos;
                instance.NewPosSelected(selectedPos);
            }

            SoundEngine.PlaySound(SoundID.MenuOpen);
        }
        catch (Exception e)
        {
            Console.WriteLine($"SpawnAnalyzerUI open exception: {e}");
        }
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
            if (!uiToggleButtonHovered)
                SoundEngine.PlaySound(SoundID.MenuTick);

            Main.LocalPlayer.mouseInterface = true;
            string text = Visible ? "Close spawn analyzer" : "Open spawn analyzer";
            Main.instance.MouseTextNoOverride(text);

            if (Main.mouseLeft && Main.mouseLeftRelease)
            {
                Toggle();
            }
        }

        uiToggleButtonHovered = hover;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (mainPanelGrabPos is null && GetAllGrabDraggableElements().Any(e => e.IsMouseHovering) && !PlayerInput.Triggers.Old.MouseLeft && PlayerInput.Triggers.Current.MouseLeft)
        {
            bool canGrab = false;
            foreach (UIElement container in GetAllGrabDraggableElements())
            {
                if (!container.IsMouseHovering)
                    continue;

                bool noContainerChildrenHover = true;
                foreach (UIElement child in container.Children)
                {
                    if (child.IsMouseHovering)
                    {
                        noContainerChildrenHover = false;
                        break;
                    }
                }
                if (noContainerChildrenHover)
                {
                    canGrab = true;
                    break;
                }
            }
            if (canGrab)
            {
                Vector2 size = SizeAbsolute;
                Vector2 topLeft = TopLeftAbsolute;
                Vector2 mouseScreen = Main.MouseScreen;

                Vector2 center = topLeft + size / 2;
                Vector2 bottomRight = size + topLeft;
                grabbedResizer = false;

                if (mouseScreen.X > center.X && mouseScreen.Y > center.Y)
                {

                    if (bottomRight.X - mouseScreen.X <= grabber.Width
                     && bottomRight.Y - mouseScreen.Y <= grabber.Height
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

        Rectangle dims = GetOuterDimensions().ToRectangle();

        Rectangle grabberRect = new(dims.Right - grabber.Width, dims.Bottom - grabber.Height, grabber.Width, grabber.Height);

        spriteBatch.Draw(grabber, grabberRect, Color.White);
    }

    IEnumerable<UIElement> GetAllGrabDraggableElements()
    {
        IEnumerable<UIElement> en = grabDragElements;
        if (currentTab is not null)
        {
            en = en.Concat(currentTab.grabDragElements);
        }
        return en;
    }

    Vector2 CalculateMinSize()
    {
        float tabsWidth = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (i > 0)
                tabsWidth += TabSpacing;
            UIElement tab = tabs[i];
            tabsWidth += tab.Width.Pixels;
        }
        
        Vector2 thisMin = new(12 + tabsWidth + 10 + 70, 200);
        if (currentTab is not null)
        {
            Vector2 tabMin = currentTab.CalculateMinSize() + new Vector2(12 * 2);
            thisMin.X = Math.Max(thisMin.X, tabMin.X);
            thisMin.Y = Math.Max(thisMin.Y, tabMin.Y);
        }
        return thisMin;
    }

    void UpdateMinSize()
    {
        // TODO: do better

        Vector2 min = CalculateMinSize();
        Width.Pixels = min.X;
        Height.Pixels = min.Y;
    }

    void UpdateTabListUI()
    {
        tabsUi.RemoveAllChildren();

        float x = 0;
        foreach (UIElement tab in tabs)
        {
            x += tab.Width.Pixels + TabSpacing;
            tabsUi.Append(tab);
        }
        float width = Math.Max(0, x - TabSpacing);

        x = -(width/2);
        foreach (UIElement tab in tabs)
        {
            tab.Left = new(x, 0.5f);
            x += tab.Width.Pixels + TabSpacing;
        }
    }

    void SelectTab(Tab? tab)
    {
        if (ReferenceEquals(currentTab, tab))
            return;

        if (currentTab is not null)
        {
            grabDragElements.Remove(currentTab);
            mainPanel.RemoveChild(currentTab);
        }

        currentTab = tab;
        if (currentTab is not null)
        {
            currentTab.Width = new(0, 1);
            currentTab.Height = new(0, 1);
            grabDragElements.Add(currentTab);
            mainPanel.Append(currentTab);
            currentTab.Recalculate();
            currentTab.TabSelected(this);
        }

        UpdateMinSize();
    }

    void NewPosSelected(Point? pos)
    {
        currentTab?.NewPosSelected(pos);
    }
}
