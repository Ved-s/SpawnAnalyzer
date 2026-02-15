using System.Linq;
using System.Reflection;
using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

public class LocalStateInfo
{
    static int GeneratedCount = 0;

    public static StateType RewriteLocalState(ILContext il, ParameterDefinition contextParam)
    {
        StateType type = StateType.Generate($"MethodLocalVarState_{GeneratedCount}", il.Body.Variables.Select(v => v.VariableType.ResolveReflection()));
        GeneratedCount++;

        ILCursor c = new(il);

        int local = 0;

        while (c.TryGotoNext(x => x.MatchLdloc(out local)))
        {
            if (local < 0)
            {
                continue;
            }
            c.Next!.OpCode = OpCodes.Ldarg;
            c.Next!.Operand = contextParam;
            c.Index += 1;
            c.Emit<SpawnSimulationContext>(OpCodes.Ldfld, "localState");
            c.Emit(OpCodes.Ldfld, il.Import(Utils.GetFieldOrThrow(type.Type, StateType.GetFieldName(local))));
        }

        c.Index = 0;

        while (c.TryGotoNext(x => x.MatchStloc(out local)))
        {
            if (local < 0)
            {
                continue;
            }
            c.Index += 1;
            c.Emit(OpCodes.Ldarg, contextParam);
            c.Emit<SpawnSimulationContext>(OpCodes.Ldfld, "localState");
            c.Emit(OpCodes.Ldloc, local);
            c.Emit(OpCodes.Stfld, il.Import(Utils.GetFieldOrThrow(type.Type, StateType.GetFieldName(local))));
        }

        return type;
    }
}

