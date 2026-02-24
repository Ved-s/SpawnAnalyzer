using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SpawnAnalyzer;

public static class HookIn
{
    public static void Entrypoint()
    {
        #if PACKAGE_LIBRARIES

        Dictionary<string, string> packagedLibs = new();

        foreach (string name in typeof(HookIn).Assembly.GetManifestResourceNames())
        {
            
            const string monoModLibPath = "SpawnAnalyzer.lib.MonoMod.";
            const string dllSuffix = ".dll";
            if (!name.StartsWith(monoModLibPath) || !name.EndsWith(dllSuffix))
                continue;
            string libName = name.Substring(monoModLibPath.Length, name.Length - monoModLibPath.Length - dllSuffix.Length);

            libName = libName.Substring(libName.IndexOf('.')+1); // strip "net48." or "net8."
            packagedLibs.Add(libName, name);
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, res) => {
            Console.Write($"Try resolve {res.Name} via SpawnAnalyzer resources => ");
            if (!packagedLibs.TryGetValue(new AssemblyName(res.Name).Name!, out string? resname))
            {
                Console.WriteLine("Not found");
                return null;
            }

            MemoryStream ms = new();
            
            typeof(HookIn).Assembly.GetManifestResourceStream(resname)!.CopyTo(ms);

            Console.WriteLine("Found");
            
            return Assembly.Load(ms.ToArray());
        };  

        #endif

        // Since we're hooked in before terraria's resolver, hook-in patch fails
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object? sender, ResolveEventArgs sargs)
        {
            Console.Write($"Try resolve {sargs.Name} via Terraria resources => ");
            string resourceName = new AssemblyName(sargs.Name).Name + ".dll";
            string? text = Array.Find(typeof(Terraria.Program).Assembly.GetManifestResourceNames(), element => element.EndsWith(resourceName));
            if (text == null)
            {
                Console.WriteLine("Not found");
                return null;
            }
            Assembly assembly;
            using (Stream manifestResourceStream = typeof(Terraria.Program).Assembly.GetManifestResourceStream(text)!)
            {
                byte[] array = new byte[manifestResourceStream.Length];
                manifestResourceStream.Read(array, 0, array.Length);
                assembly = Assembly.Load(array);
            }
            Console.WriteLine("Found");
            return assembly;
        };

        Assembly.Load("ReLogic, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        HookInPatches.HookEntrypoint();
    }
}