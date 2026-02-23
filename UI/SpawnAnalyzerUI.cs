using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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


    UIPanel mainPanel;

    UIItemList spawnButtonsContainer;
    UIVerticalScrollArea spawnButtonsContainerScroll;

    UIPanel sidePanel;
    UIElement? sidePanelTraits;

    Selection<NPCSpawnButton> spawnButtonSelection;

    HashSet<UIElement> grabDragElements = new();

    Texture2D grabber = SpawnAnalyzer.GetTexture("UIGrabber");

    const float SidePanelWidth = 200;

    public SpawnAnalyzerUI()
    {
        Top = new(0f, .35f);
        Left = new(0f, .35f);
        Width = new(SidePanelWidth + 12 * 2 + 10 * 2 + NPCSpawnButton.FixedWidth + 20f, .3f);
        Height = new(200, .3f);

        mainPanel = new()
        {
            Width = new(0, 1),
            Height = new(0, 1),
        };

        spawnButtonsContainer = new()
        {
            AutoHeight = true,
        };

        spawnButtonsContainerScroll = new(spawnButtonsContainer)
        {
            Width = new(-SidePanelWidth - 10, 1),
            Height = new(0, 1),
        };

        mainPanel.Append(spawnButtonsContainerScroll);

        sidePanel = new()
        {
            Left = new(-SidePanelWidth, 1),
            Height = new(0, 1),
            Width = new(SidePanelWidth, 0),
        };
        sidePanel.SetPadding(6);

        mainPanel.Append(sidePanel);

        Append(mainPanel);

        spawnButtonSelection = new();
        spawnButtonSelection.OnSelectionChanged += OnSpawnButtonSelected;

        grabDragElements.Add(mainPanel);
        grabDragElements.Add(spawnButtonsContainer);
        grabDragElements.Add(spawnButtonsContainerScroll);
        grabDragElements.Add(sidePanel);
    }

    public static void Open()
    {
        if (Visible)
            return;

        instance ??= new();

        ui.SetState(instance);

        if (lastSelectedPos != selectedPos)
        {
            lastSelectedPos = selectedPos;
            instance.NewPosSelected(selectedPos);
        }

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

        if (mainPanelGrabPos is null && grabDragElements.Any(e => e.IsMouseHovering) && !PlayerInput.Triggers.Old.MouseLeft && PlayerInput.Triggers.Current.MouseLeft)
        {
            bool canGrab = false;
            foreach (UIElement container in grabDragElements)
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

    void NewPosSelected(Point? pos)
    {
        int? selectednpcid = spawnButtonSelection.CurrentSelection?.spawn.id;
        spawnButtonsContainer.RemoveAllChildren();
        bool clearSelection = true;

        if (pos is not null && (SpawnAnalyzer.LastAnalysis?.results.TryGetValue(pos.Value, out var posDict) ?? false))
        {
            foreach (var spawn in posDict.Values.OrderByDescending(s => s.spawns.Max(s => s.chance)))
            {
                NPCSpawnButton button = new(spawn, spawnButtonSelection);

                spawnButtonsContainer.Append(button);

                if (selectednpcid == spawn.id)
                {
                    spawnButtonSelection.CurrentSelection = button;
                    clearSelection = false;
                }

            }
        }

        spawnButtonsContainer.RecalculateChildren();
        if (clearSelection)
            spawnButtonSelection.CurrentSelection = null;
    }

    void OnSpawnButtonSelected(NPCSpawnButton? button)
    {
        if (sidePanelTraits is not null)
            grabDragElements.Remove(sidePanelTraits);

        sidePanel.RemoveAllChildren();
        sidePanelTraits = null;

        if (button is null)
            return;

        var mspawn = button.spawn;

        sidePanel.Append(new UIText(Lang.GetNPCName(mspawn.id))
        {
            Top = new(4, 0),
            Width = new(0, 1),
            Height = new(30, 0),
        });

        sidePanel.Append(new UIEntityIcon(new UnlockableNPCEntryIcon(mspawn.id))
        {
            Top = new(20, 0),
            Width = new(0, 1),
            Height = new(64, 0),
            ForceHover = true,
        });

        float y = 90;

        for (int i = 0; i < mspawn.spawns.Count; i++)
        {
            AnalyzedSpawn spawn = mspawn.spawns[i];

            if (i > 0)
            {
                int spawnnum = i + 1;
                string spawnnumsuffix = (spawnnum % 10) switch
                {
                    1 => "st",
                    2 => "nd",
                    3 => "rd",
                    _ => "th"
                };

                if (spawnnum > 10 && spawnnum <= 20)
                {
                    spawnnumsuffix = "th";
                }

                y += 10;

                sidePanel.Append(new UIText($"{spawnnum}{spawnnumsuffix} spawn:")
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(30, 0),
                    TextOriginX = 0,
                });

                y += 20;
            }

            sidePanel.Append(new UIText($"Chance:")
            {
                Top = new(y, 0),
                Width = new(0, 1),
                Height = new(30, 0),
                TextOriginX = 0,
            });

            sidePanel.Append(new UIText($"{spawn.chance * 100:0.0000}%")
            {
                Top = new(y, 0),
                Width = new(0, 1),
                Height = new(30, 0),
                TextOriginX = 1,
            });

            y += 26;

            if (spawn.affectedByLuck)
            {
                UIPanel luckTraitPanel = new()
                {
                    Width = new(0, 1),
                    Height = new(32, 0),
                    Top = new(y, 0),
                };
                y += luckTraitPanel.Height.Pixels + 10;
                luckTraitPanel.SetPadding(0);
                sidePanel.Append(luckTraitPanel);

                Texture2D luck = SpawnAnalyzer.GetTexture("Luck");

                luckTraitPanel.Append(new UIImage(luck)
                {
                    Top = new(0, 0),
                    Left = new(2, 0),
                    RemoveFloatingPointsFromDrawPosition = true,
                });

                luckTraitPanel.Append(new UIText("Affected by luck")
                {
                    Top = new(8, 0),
                    Left = new(0, 0),
                    Width = new(-6, 1),
                    Height = new(30, 0),
                    TextOriginX = 1,
                });
            }
        }
    }
}
