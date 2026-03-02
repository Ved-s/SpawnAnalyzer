using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace SpawnAnalyzer.UI.Tabs;

public class FinalSpawnsTab: Tab
{
    UIItemList spawnButtonsContainer;
    UIVerticalScrollArea spawnButtonsContainerScroll;

    UIPanel sidePanel;
    UIElement? sidePanelTraits;

    Selection<NPCSpawnButton> spawnButtonSelection;

    const float SidePanelWidth = 200;

    public FinalSpawnsTab()
    {
        spawnButtonsContainer = new()
        {
            AutoHeight = true,
        };

        spawnButtonsContainerScroll = new(spawnButtonsContainer)
        {
            Width = new(-SidePanelWidth - 10, 1),
            Height = new(0, 1),
        };

        Append(spawnButtonsContainerScroll);

        sidePanel = new()
        {
            Left = new(-SidePanelWidth, 1),
            Height = new(0, 1),
            Width = new(SidePanelWidth, 0),
        };
        sidePanel.SetPadding(6);

        Append(sidePanel);

        spawnButtonSelection = new();
        spawnButtonSelection.OnSelectionChanged += OnSpawnButtonSelected;

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

    override public void NewPosSelected(Point? pos)
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
