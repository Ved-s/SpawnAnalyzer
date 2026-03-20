using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public class SpawnsTab : Tab
{
    UIText headerText;

    UIElement pageContainer;

    UIVerticalScrollArea spawnButtonsContainerScroll;
    UIItemList spawnButtonsContainer;

    UIPanel sidePanel;
    UIElement? sidePanelTraits;

    Selection<NPCSpawnButton> spawnButtonSelection;

    UIButton[] displayModeButtons;
    Selection<UIButton> displayModeSelection;

    DisplayMode? displayMode = DisplayMode.Final;

    const float SidePanelWidth = 200;

    public SpawnsTab()
    {
        displayModeSelection = new();
        displayModeSelection.OnSelectionChanged += OnDisplayModeButtonSelected;

        displayModeButtons = new UIButton[3];

        Append(new UIText("Display mode")
        {
            Top = new(15, 0),
            Left = new(10, 0),
            Width = new(120, 0),
            Height = new(25, 0),
            TextOriginX = 0,
        });

        for (int i = 0; i < 3; i++)
        {
            DisplayMode mode = (DisplayMode)i;

            string hoverText = mode switch
            {
                DisplayMode.Starting => "Display which NPCs start their spawning on selected tile.\nFor example dragonflies start their spawning underwater,\nbut actually spawn at the nearest cattail.",
                DisplayMode.Final => "Display which NPCs actually spawn on selected tile",
                DisplayMode.Total => "Display which NPCs spawn on all found spawning tiles in the spawn area",
                _ => throw new IndexOutOfRangeException()
            };

            UIButton button = new(mode.ToString())
            {
                Top = new(9, 0),
                Left = new(6 + 120 + i * 100, 0),
                Width = new(95, 0),
                Height = new(30, 0),
                Selection = displayModeSelection,
                Tag = mode,
                HoverText = hoverText,
            };
            displayModeButtons[i] = button;
            Append(button);
        }

        headerText = new("")
        {
            TextOriginX = 0,
            TextOriginY = 0,

            Top = new(44, 0),
            Left = new(10, 0),
            Width = new(-20, 1),
            Height = new(20, 0),
        };
        Append(headerText);

        float pageTop = 9 + 25 + 35;

        Append(new UIImage(TextureAssets.MagicPixel)
        {
            Color = Color.Black * 0.8f,

            Top = new(pageTop, 0),
            Left = new(4, 0),
            Width = new(-8, 1),
            Height = new(2, 0),

            AllowResizingDimensions = false,
            ScaleToFit = true,
        });

        pageContainer = new()
        {
            Top = new(pageTop, 0),
            Left = new(0, 0),
            Width = new(0, 1),
            Height = new(-pageTop, 1),
        };
        pageContainer.SetPadding(12);
        Append(pageContainer);

        spawnButtonsContainer = new()
        {
            AutoHeight = true,
        };

        spawnButtonsContainerScroll = new(spawnButtonsContainer)
        {
            Width = new(-SidePanelWidth - 10, 1),
            Height = new(0, 1),
        };

        pageContainer.Append(spawnButtonsContainerScroll);

        sidePanel = new()
        {
            Left = new(-SidePanelWidth, 1),
            Height = new(0, 1),
            Width = new(SidePanelWidth, 0),
        };
        sidePanel.SetPadding(6);

        pageContainer.Append(sidePanel);

        spawnButtonSelection = new();
        spawnButtonSelection.OnSelectionChanged += OnSpawnButtonSelected;

        grabDragElements.Add(pageContainer);
        grabDragElements.Add(spawnButtonsContainer);
        grabDragElements.Add(spawnButtonsContainerScroll);
        grabDragElements.Add(sidePanel);

        UpdateDisplayModeButtons();
    }

    public override Vector2 CalculateMinSize()
    {
        float pageMinWidth = SidePanelWidth + 10 * 2 + NPCSpawnButton.FixedWidth + 20f;
        float displaymodeButtonsminWidth = 0;
        foreach (UIButton button in displayModeButtons)
        {
            displaymodeButtonsminWidth = Math.Max(displaymodeButtonsminWidth, button.Left.Pixels + button.Width.Pixels);
        }
        return new(
            Math.Max(pageMinWidth, displaymodeButtonsminWidth),
            0
        );
    }

    public override void OnEvent(SpawnAnalyzerUIEvent ev)
    {
        base.OnEvent(ev);

        switch (ev)
        {
            case NewSpawnAnalysisEvent:
                UpdateDisplayModeButtons();
                if (displayMode == DisplayMode.Total)
                    RebuildSpawnsList();
                break;

            case NewPosSelectedEvent:
                UpdateDisplayModeButtons();
                if (displayMode != DisplayMode.Total)
                    RebuildSpawnsList();
                break;
        }
    }

    void RebuildSpawnsList()
    {
        int? selectednpcid = spawnButtonSelection.CurrentSelection?.spawn.id;
        spawnButtonsContainer.RemoveAllChildren();
        bool clearSelection = true;

        Dictionary<int, AnalyzedMultiSpawn>? spawns = null;

        SpawnAnalysisResults? results = SpawnAnalyzer.LastAnalysis?.results;

        Point? pos = SpawnAnalyzerUI.SelectedPos;

        if (results is not null)
        {
            switch (displayMode)
            {
                case DisplayMode.Starting:
                    if (pos is not null)
                        results.starting.TryGetValue(pos.Value, out spawns);
                    break;

                case DisplayMode.Final:
                    if (pos is not null)
                        results.final.TryGetValue(pos.Value, out spawns);
                    break;

                case DisplayMode.Total:
                    spawns = results.total;
                    break;
            }
        }

        if (spawns is not null)
        {
            foreach (var spawn in spawns.Values.OrderByDescending(s => s.spawns.Max(s => s.chance)))
            {
                NPCSpawnButton button = new(spawn, spawnButtonSelection);

                spawnButtonsContainer.Append(button);

                if (selectednpcid == spawn.id)
                {
                    spawnButtonSelection.CurrentSelection = button;
                    clearSelection = false;
                }
            }

            string? spawnLocation = null;

            switch (displayMode)
            {
                case DisplayMode.Final:
                    if (pos is not null)
                    {
                        string tileName = TileID.Search.GetName(Main.tile[pos.Value.X, pos.Value.Y].type);
                        spawnLocation = $"spawning on {tileName} at {pos.Value.X}, {pos.Value.Y}";
                    }
                    break;

                case DisplayMode.Starting:
                    if (pos is not null)
                    {
                        string tileName = TileID.Search.GetName(Main.tile[pos.Value.X, pos.Value.Y].type);
                        spawnLocation = $"begin spawning on {tileName} at {pos.Value.X}, {pos.Value.Y}";
                    }
                    break;

                case DisplayMode.Total:
                    spawnLocation = $"spawning in the area";
                    break;
            }

            if (spawnLocation is null)
            {
                headerText.SetText("");
            }
            else
            {
                string s = "s";
                if (spawns.Count == 1)
                    s = "";

                headerText.SetText($"{spawns.Count} NPC{s} {spawnLocation}");
            }
        }
        else
        {
            headerText.SetText("");
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

    void UpdateDisplayModeButtons()
    {
        if (displayMode is not null && !IsDisplayModeValid(displayMode.Value) || displayMode is null)
        {
            displayMode = null;

            foreach (UIButton button in displayModeButtons)
            {
                if (button.Tag is not DisplayMode mode)
                    continue;

                if (!IsDisplayModeValid(mode))
                    continue;

                displayModeSelection.CurrentSelection = button;
                break;
            }

            if (displayMode is null)
                displayModeSelection.CurrentSelection = null;
        }

        foreach (UIButton button in displayModeButtons)
        {
            if (button.Tag is not DisplayMode mode)
                continue;

            if (IsDisplayModeValid(mode))
            {
                if (button.Disabled)
                {
                    button.Disabled = false;
                    button.SetNewText(color: Color.White);
                }
            }
            else
            {
                if (!button.Disabled)
                {
                    button.Disabled = true;
                    button.SetNewText(color: Color.Gray);
                }
            }
        }
    }

    void OnDisplayModeButtonSelected(UIButton? button)
    {
        DisplayMode? mode = null;

        if (button is not null)
        {
            if (button.Tag is DisplayMode mode2)
            {
                mode = mode2;
            }
            else
            {
                return;
            }
        }

        if (mode is not null && !IsDisplayModeValid(mode.Value))
            return;

        displayMode = mode;
        RebuildSpawnsList();
    }

    bool IsDisplayModeValid(DisplayMode mode)
    {
        SpawnAnalysisResults? results = SpawnAnalyzer.LastAnalysis?.results;
        if (results is null)
            return false;

        switch (mode)
        {
            case DisplayMode.Starting:
                return SpawnAnalyzerUI.SelectedPos is not null && results.starting.ContainsKey(SpawnAnalyzerUI.SelectedPos.Value);

            case DisplayMode.Final:
                return SpawnAnalyzerUI.SelectedPos is not null && results.final.ContainsKey(SpawnAnalyzerUI.SelectedPos.Value);

            case DisplayMode.Total:
                return true;
        }

        return false;
    }

    enum DisplayMode
    {
        Starting,
        Final,
        Total
    }
}
