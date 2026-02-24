using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace SpawnAnalyzer;

internal static class HookInPatches
{
    static object? ProgramLaunchGameHook = null; 

    internal static void HookEntrypoint()
    {
        ProgramLaunchGameHook = new MonoMod.RuntimeDetour.ILHook(
            typeof(Terraria.Program).GetMethod("LaunchGame", (BindingFlags)(-1)) ?? throw new MissingMethodException("Terraria.Program.LaunchGame"), 
            il =>
            {
                ILCursor c = new(il);

                /* 
                    +call      void MainPatch::Install1()
                     call      void Terraria.Program::RunGame()
                */
                if (!c.TryGotoNext(
                    x => x.MatchCall("Terraria.Program", "RunGame")
                ))
                {
                    Console.WriteLine("Prepatch match error");
                    Environment.Exit(1);
                    return;
                }

                c.Emit<SpawnAnalyzer>(OpCodes.Call, nameof(SpawnAnalyzer.InstallVanilla));
            }
        );
    }
}