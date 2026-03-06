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

public class FinalSpawnsTab: Tab
{
    UIText headerText;
    UIPanel reAnalyzeButton;

    UIElement pageContainer;

    UIVerticalScrollArea spawnButtonsContainerScroll;
    UIItemList spawnButtonsContainer;

    UIPanel sidePanel;
    UIElement? sidePanelTraits;

    Selection<NPCSpawnButton> spawnButtonSelection;

    const float SidePanelWidth = 200;

    const float ReCalculateButtonWidth = 102;

    public FinalSpawnsTab()
    {
        headerText = new("")
        {
            TextOriginX = 0,
            TextOriginY = 0,

            Top = new(9, 0),
            Left = new(6, 0),
            Width = new(-(ReCalculateButtonWidth + 10), 1),
            Height = new(20, 0),
        };
        Append(headerText);

        reAnalyzeButton = new()
        {
            Width = new(ReCalculateButtonWidth, 0),
            Height = new(28, 0),
            Top = new(4, 0),
            Left = new(-(ReCalculateButtonWidth + 4), 1),
        };
        reAnalyzeButton.SetPadding(0);
        reAnalyzeButton.OnLeftClick += (_, _) =>
        {
            SpawnAnalyzer.LastAnalysis?.Simulate();
            NewPosSelected(SpawnAnalyzerUI.SelectedPos);
        };
        reAnalyzeButton.OnMouseOver += (_, _) => {
            reAnalyzeButton.BackgroundColor = new Color(83, 102, 171) * 0.7f;
            SoundEngine.PlaySound(SoundID.MenuTick);
        };
        reAnalyzeButton.OnMouseOut += (_, _) => {
            reAnalyzeButton.BackgroundColor = new Color(63, 82, 151) * 0.7f;
        };

        reAnalyzeButton.Append(new UIText("Re-analyze")
        {
            Width = new(0, 1),
            Height = new(0, 1),
            TextOriginX = 0.5f,
            TextOriginY = 0.5f,
        });

        Append(new UIImage(TextureAssets.MagicPixel)
        {
            Color = Color.Black * 0.8f,

            Top = new(34, 0),
            Left = new(4, 0),
            Width = new(-8, 1),
            Height = new(2, 0),

            AllowResizingDimensions = false,
            ScaleToFit = true,
        });

        pageContainer = new()
        {
            Top = new(34, 0),
            Left = new(0, 0),
            Width = new(0, 1),  
            Height = new(-34, 1),  
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

        grabDragElements.Add(headerText);
        grabDragElements.Add(pageContainer);
        grabDragElements.Add(spawnButtonsContainer);
        grabDragElements.Add(spawnButtonsContainerScroll);
        grabDragElements.Add(sidePanel);
    }

    public override Vector2 CalculateMinSize()
    {
        return new(
            SidePanelWidth + 10 * 2 + NPCSpawnButton.FixedWidth + 20f,
            0
        );
    }

    public override void NewPosSelected(Point? pos)
    {
        base.NewPosSelected(pos);

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

            string tileName = TileID.Search.GetName(Main.tile[pos.Value.X, pos.Value.Y].type);
            
            string s = "s";
            if (posDict.Count == 1)
                s = "";

            headerText.SetText($"{posDict.Count} NPC{s} spawning on {tileName} at {pos.Value.X}, {pos.Value.Y}");
            if (reAnalyzeButton.Parent is null)
            {
                Append(reAnalyzeButton);
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
}
