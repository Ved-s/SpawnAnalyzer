This is a mod that analyzes Terraria's NPC spawning system by copying game's spawning code and rewriting it to then run it and analyze its structure.

The mod is for Terraria 1.4.5 and since tModLoader doesn't support it (yet), so there's no mod structure yet and you have to manually inject it into the game.


Currently you have to press Z to analyze spawns, UI is in-progress, the button to open it is under the inventory


# Building

Prerequisites: .NET 8 SDK

Open `Configuration.prop` and set the path to your Terraria directory and your installation type.
```xml
<!-- path to the Terraria directory --> 
<TerrariaPath> -- Write path here -- </TerrariaPath>

<!-- valid values: vanillaWindows, vanillaNonWindows, tModLoaderNetcore --> 
<TerrariaFlavor> -- Write installation type here -- </TerrariaFlavor>
```

Build with `dotnet build`. `SpawnAnalyzer.dll` will be copied to the specified Terraria directory.


# Injecting into the game

`SpawnAnalyzer.dll` must be in the same directory as Terraria itself.

1. Download dnSpyEx from https://github.com/dnSpyEx/dnSpy/releases
2. Open it
3. Navigate to your Terraria directory in a file explorer
4. Drag and drop your Terraria executable (`Terraria.exe` or `TerrariaRelease.dll`) into the Assembly Explorer
    > If `Assembly Explorer` is hidden, open it through `View > Assembly Explorer`
5. Drag and drop SpawnAnalyzer.dll into the Assembly Explorer
6. Click on `Terraria` in the assembly explorer
7. In the code editor window, click on the entry point, usually called `Main`
8. Right-click on the opened method's name and select `Edit IL Instructions`
9. In the newly opened window, scroll to the top, right-click the first instruction and select `Add new instruction before selection`
10. Click on the appeared `nop`, type `call` and select that
11. Click on the `null` that appeared to the right of it, pick `Method`
12. In the search box, type or paste `HookIn.Entrypoint` and click on the appeared result
13. Click Ok in the method selector, then click Ok in the instruction editor
14. `HookIn.Entrypoint();` should appear under the method's name
15. Click File, then Save Module, then Ok


(Images are todo, write me or create an issue if it's too complicated and you need visual guide)
