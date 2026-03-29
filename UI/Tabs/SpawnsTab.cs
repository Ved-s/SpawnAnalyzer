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

    UIButton[] chanceModeButtons;
    Selection<UIButton> chanceModeSelection;

    UIText nonDeterministicText;

    DisplayMode? displayMode = null;
    ChanceMode? chanceMode = null;

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
                DisplayMode.Area => "Display which NPCs spawn on all found spawning tiles in the spawn area",
                _ => throw new IndexOutOfRangeException()
            };

            string name = mode switch
            {
                DisplayMode.Starting => "Initial",
                DisplayMode.Final => "Tile",
                DisplayMode.Area => "Area",
                _ => throw new IndexOutOfRangeException()
            };

            UIButton button = new(name)
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

        chanceModeSelection = new();
        chanceModeSelection.OnSelectionChanged += OnChanceModeButtonSelected;
        chanceModeButtons = new UIButton[2];

        Append(new UIText("Chance mode")
        {
            Top = new(50, 0),
            Left = new(10, 0),
            Width = new(120, 0),
            Height = new(25, 0),
            TextOriginX = 0,
        });

        for (int i = 0; i < 2; i++)
        {
            ChanceMode mode = (ChanceMode)i;

            string name = mode switch
            {
                ChanceMode.Chance => "Chance",
                ChanceMode.Time => "Average time",
                _ => throw new IndexOutOfRangeException()
            };

            UIButton button = new(name)
            {
                Top = new(44, 0),
                Left = new(6 + 120 + i * 150, 0),
                Width = new(145, 0),
                Height = new(30, 0),
                Selection = chanceModeSelection,
                Tag = mode,
            };
            chanceModeButtons[i] = button;
            Append(button);
        }

        nonDeterministicText = new("Warning! Some spawn entries will not show up or\nhave weird data due to side-effects in the code")
        {
            Top = new(15, 0),
            Left = new(6 + 120 + 300 + 10, 0),
            Width = new(-(6 + 120 + 300 + 20), 1),
            Height = new(60, 0),
            TextColor = Color.Orange,
            TextOriginX = 0,
            OverflowHidden = true,
            MinWidth = new(0, 0),
        };

        headerText = new("")
        {
            TextOriginX = 0,
            TextOriginY = 0,

            Top = new(9 + 35 + 35, 0),
            Left = new(10, 0),
            Width = new(-20, 1),
            Height = new(20, 0),
        };
        Append(headerText);

        float pageTop = 9 + 25 + 35 + 35;

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
            OverflowHidden = true,
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
        UpdateChanceModeButtons();
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
                UpdateNonDeterministicWarning();
                if (displayMode == DisplayMode.Area)
                    RebuildSpawnsList();
                break;

            case NewPosSelectedEvent:
                UpdateDisplayModeButtons();
                UpdateNonDeterministicWarning();
                if (displayMode != DisplayMode.Area)
                    RebuildSpawnsList();
                break;
        }
    }

    void UpdateNonDeterministicWarning()
    {
        if (nonDeterministicText.Parent is not null)
            nonDeterministicText.Remove();

        bool showWarning = false;

        SpawnAnalysis? analysis = SpawnAnalyzer.LastAnalysis;

        if (analysis is not null)
        {
            switch (displayMode)
            {
                case DisplayMode.Starting:
                    if (SpawnAnalyzerUI.SelectedPos is not null && analysis.foundSpawnSpots.TryGetValue(SpawnAnalyzerUI.SelectedPos.Value, out var spot))
                    {
                        showWarning = spot.NonDeterministic;
                    }
                    break;

                default:
                    showWarning = analysis.NonDeterministic;
                    break;
            }
        }

        if (showWarning)
            Append(nonDeterministicText);
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

                case DisplayMode.Area:
                    spawns = results.total;
                    break;
            }
        }

        if (spawns is not null)
        {
            foreach (var spawn in spawns.Values.OrderByDescending(s => s.spawns.Max(s => s.chance)))
            {
                NPCSpawnButton button = new(spawn, spawnButtonSelection)
                {
                    ShowChanceAsAverageTime = chanceMode == ChanceMode.Time
                };

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

                case DisplayMode.Area:
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

        sidePanel.Append(new UIEntityIcon(new UnlockableNPCEntryIcon(mspawn.id))
        {
            Top = new(20, 0),
            Width = new(0, 1),
            Height = new(64, 0),
            ForceHover = true,
        });

        sidePanel.Append(new UIText(Lang.GetNPCName(mspawn.id))
        {
            Top = new(4, 0),
            Width = new(0, 1),
            Height = new(30, 0),
        });

        float y = 90;

        sidePanel.Append(new UIText($"Type: {mspawn.id}")
        {
            Top = new(y, 0),
            Width = new(0, 1),
            Height = new(30, 0),
            TextOriginX = 0,
        });

        y += 22;

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

                sidePanel.Append(new UIImage(TextureAssets.MagicPixel)
                {
                    Color = Color.Black * 0.8f,

                    Top = new(y, 0),
                    Left = new(0, 0),
                    Width = new(0, 1),
                    Height = new(2, 0),

                    AllowResizingDimensions = false,
                    ScaleToFit = true,
                });

                y += 10;

                sidePanel.Append(new UIText($"{spawnnum}{spawnnumsuffix} spawn:")
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(30, 0),
                    TextOriginX = 0,
                });

                y += 22;
            }

            sidePanel.Append(new UIText($"Chance:")
            {
                Top = new(y, 0),
                Width = new(0, 1),
                Height = new(30, 0),
                TextOriginX = 0,
            });

            string maybeLess = spawn.leaked switch
            {
                true => "<",
                false => "",
            };
            Color chanceColor = spawn.leaked switch
            {
                true => Color.Red,
                false => Color.White,
            };

            sidePanel.Append(new UIText($"{maybeLess}{spawn.chance * 100:0.0000}%")
            {
                Top = new(y, 0),
                Width = new(0, 1),
                Height = new(30, 0),
                TextOriginX = 1,
                TextColor = chanceColor,
            });

            y += 20;

            if (displayMode != DisplayMode.Starting)
            {
                string maybeMore = spawn.leaked switch
                {
                    true => ">",
                    false => "",
                };

                double avgTicks = 1 / (double)spawn.chance;
                double avgSeconds = avgTicks / 60;

                sidePanel.Append(new UIText($"Avg. time:")
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(30, 0),
                    TextOriginX = 0,
                });

                sidePanel.Append(new UIText(maybeMore + NPCSpawnButton.FormatTimeShort(avgSeconds))
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(30, 0),
                    TextOriginX = 1,
                    TextColor = chanceColor,
                });

                y += 20;
            }

            if (spawn.leaked)
            {
                sidePanel.Append(new UIText("Could not determine")
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(20, 0),
                    TextOriginX = 0,
                    TextColor = Color.Orange,
                });
                y += 20;
                sidePanel.Append(new UIText("exact chance")
                {
                    Top = new(y, 0),
                    Width = new(0, 1),
                    Height = new(20, 0),
                    TextOriginX = 0,
                    TextColor = Color.Orange,
                });
                y += 20;
            }

            y += 6;

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

            if (spawn.spawnOnPlayer)
            {
                UIPanel traitPanel = new()
                {
                    Width = new(0, 1),
                    Height = new(32, 0),
                    Top = new(y, 0),
                };
                y += traitPanel.Height.Pixels + 10;
                traitPanel.SetPadding(0);
                sidePanel.Append(traitPanel);

                Main.instance.LoadItem(ItemID.SlimeCrown);
                Texture2D icon = TextureAssets.Item[ItemID.SlimeCrown].Value;

                traitPanel.Append(new UIImage(icon)
                {
                    Top = new(16 - icon.Height / 2, 0),
                    Left = new(2 + 16 - icon.Width / 2, 0),
                    RemoveFloatingPointsFromDrawPosition = true,
                });

                traitPanel.Append(new UIText("Spawns in area")
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
        UpdateChanceModeButtons();
        UpdateNonDeterministicWarning();
    }

    void UpdateChanceModeButtons()
    {
        if (chanceMode is not null && !IsChanceModeValid(chanceMode.Value) || chanceMode is null)
        {
            chanceMode = null;

            foreach (UIButton button in chanceModeButtons)
            {
                if (button.Tag is not ChanceMode mode)
                    continue;

                if (!IsChanceModeValid(mode))
                    continue;

                chanceModeSelection.CurrentSelection = button;
                break;
            }

            if (chanceMode is null)
                chanceModeSelection.CurrentSelection = null;
        }

        foreach (UIButton button in chanceModeButtons)
        {
            if (button.Tag is not ChanceMode mode)
                continue;

            if (IsChanceModeValid(mode))
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

            button.HoverText = null;

            if (mode == ChanceMode.Time)
            {
                if (displayMode == DisplayMode.Final)
                    button.HoverText = "Display average time it takes to NPC to spawn on the\nselected tile each spawn tick (affected by spawn rate)";
                else if (displayMode == DisplayMode.Area)
                    button.HoverText = "Display average time it takes to NPC to spawn in the\nentire spawn area (affected by spawn rate)";
            }
            else if (mode == ChanceMode.Chance)
            {
                if (displayMode == DisplayMode.Starting)
                    button.HoverText = "Display chance of the NPC spawning when that tile\nis chosen for spawning";
                else if (displayMode == DisplayMode.Final)
                    button.HoverText = "Display chance of NPC spawning on the selected tile\neach tick (affected by spawn rate)";
                else if (displayMode == DisplayMode.Area)
                    button.HoverText = "Display chance of NPC spawning in the entire spawn\narea each tick (affected by spawn rate).";
            }
        }
    }

    void OnChanceModeButtonSelected(UIButton? button)
    {
        ChanceMode? mode = null;

        if (button is not null)
        {
            if (button.Tag is ChanceMode mode2)
            {
                mode = mode2;
            }
            else
            {
                return;
            }
        }

        if (mode is not null && !IsChanceModeValid(mode.Value))
            return;

        chanceMode = mode;
        UpdateSpawnButtonsChanceMode();
    }

    void UpdateSpawnButtonsChanceMode()
    {
        foreach (UIElement elem in spawnButtonsContainer.Children)
        {
            if (elem is not NPCSpawnButton button)
                continue;

            button.ShowChanceAsAverageTime = chanceMode == ChanceMode.Time;
        }
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

            case DisplayMode.Area:
                return true;
        }

        return false;
    }

    bool IsChanceModeValid(ChanceMode mode)
    {
        if (displayMode is null)
            return false;

        switch (mode)
        {
            case ChanceMode.Chance:
                return true;

            case ChanceMode.Time:
                return displayMode != DisplayMode.Starting;
        }

        return false;
    }

    enum DisplayMode
    {
        Starting,
        Final,
        Area
    }

    enum ChanceMode
    {
        Time,
        Chance
    }
}
