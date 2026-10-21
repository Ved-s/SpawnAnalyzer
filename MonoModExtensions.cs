using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria.GameContent.ItemDropRules;

namespace SpawnAnalyzer;

static class MonoModExtenstions
{
    public static void RetargetLabel(this ILCursor c, ILLabel label, Instruction to) => c.Context.RetargetLabels(label.Target!, to);
    public static void RetargetLabel(this ILContext il, ILLabel label, Instruction to) => il.RetargetLabels(label.Target!, to);

    public static void RetargetLabels(this ILCursor c, Instruction from, Instruction to) => c.Context.RetargetLabels(from, to);
    public static void RetargetLabels(this ILContext il, Instruction from, Instruction to)
    {
        foreach (ILLabel label in il.Labels)
        {
            if (label.Target == from)
                label.Target = to;
        }
    }

    public static void FancyPrintout(this ILContext il, List<InstructionStackInfo>? stack = null)
    {
        Console.Write(DnSpyAnsiColors.ildirective);
        Console.Write(".locals ");
        Console.Write(DnSpyAnsiColors.keyword);
        Console.Write("init ");

        if (il.Body.Variables.Count == 0)
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.WriteLine("()");
        }
        else
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.WriteLine("(");

            for (int i = 0; i < il.Body.Variables.Count; i++)
            {
                if (i > 0)
                {
                    Console.Write(DnSpyAnsiColors.punctuation);
                    Console.WriteLine(",");
                }
                Console.Write("\t");
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write("[");
                Console.Write(DnSpyAnsiColors.number);
                Console.Write(i);
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write("] ");
                AssemblyPrint.Print(il.Body.Variables[i].VariableType);
            }
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.WriteLine("\n)");


        }

        int maxIndexWidth = (int)Math.Log10(il.Instrs.Count) + 1;
        int maxstackdepth = 0;
        if (stack is not null)
        {
            foreach (var info in stack)
            {
                maxstackdepth = Math.Max(info.inValues.Count, Math.Max(maxstackdepth, info.outValues.Count));
            }
        }

        for (int i = 0; i < il.Instrs.Count; i++)
        {
            Console.Write(DnSpyAnsiColors.comment);
            Console.Write("/* ");
            Console.Write(i);
            int indexWidth = (i == 0) ? 1 : (int)Math.Log10(i) + 1;
            for (int j = 0; j < (maxIndexWidth - indexWidth); j++)
                Console.Write(" ");

            if (stack is not null && stack[i].instruction == il.Instrs[i])
            {
                Console.Write(" ");

                var info = stack[i];
                int inv = info.inValues.Count;
                int outv = info.outValues.Count;

                int commonvalues = 0;

                for (int j = 0; j < Math.Min(inv, outv); j++)
                {
                    if (info.inValues[j] == info.outValues[j])
                    {
                        commonvalues++;
                    }
                    else
                    {
                        break;
                    }
                }

                for (int j = 0; j < Math.Min(inv, outv); j++)
                {
                    if (j >= commonvalues)
                        Console.Write(">");
                    else
                        Console.Write("|");
                }

                if (inv < outv)
                {
                    for (int j = 0; j < (outv-inv); j++)
                    {
                        Console.Write("/");
                    }
                }
                else
                {
                    for (int j = 0; j < (inv-outv); j++)
                    {
                        Console.Write("\\");
                    }
                }

                for (int j = Math.Max(info.inValues.Count, info.outValues.Count); j < maxstackdepth; j++)
                {
                    Console.Write("-");
                }
            }
            
            Console.Write(" */ ");
            AssemblyPrint.Print(il.Instrs[i]);
            Console.WriteLine();
            FlowControl flowControl = il.Instrs[i].OpCode.FlowControl;
            if (flowControl != FlowControl.Next && flowControl != FlowControl.Call)
            {
                Console.WriteLine();
            }
        }
        Console.Write(DnSpyAnsiColors.reset);

    }

    public static void PrepareMethodForManipulation(this ILContext il) {
        foreach (Instruction instr in il.Instrs)
		{
			if (instr.Operand is Instruction target)
			{
				instr.Operand = il.DefineLabel(target);
			}
			else if (instr.Operand is Instruction[] source)
			{
				instr.Operand = source.Select(t => il.DefineLabel(t)).ToArray();
			}
		}
    }

    public static void FinishMethodAfterManipulation(this ILContext il) {
        foreach (Instruction instr2 in il.Instrs)
		{
			if (instr2.Operand is ILLabel iLLabel)
			{
				instr2.Operand = iLLabel.Target;
			}
			else if (instr2.Operand is ILLabel[] source2)
			{
				instr2.Operand = source2.Select(l => l.Target).ToArray();
			}
		}
		il.Method.FixShortLongOps();
    }

    public static void FillWithFakeILOffsets(this Mono.Collections.Generic.Collection<Instruction> collection) {
        for (int i = 0; i < collection.Count; i++)
        {
            collection[i].Offset = i;
        }
    }

    public static bool MatchLocalOpcode(this Instruction ins, out OpcodeValueAction type, out int local) {
        if (ins.MatchLdloc(out local)) {
            type = OpcodeValueAction.Load;
            return true;
        }

        if (ins.MatchLdloca(out local)) {
            type = OpcodeValueAction.LoadRef;
            return true;
        }

        if (ins.MatchStloc(out local)) {
            type = OpcodeValueAction.Store;
            return true;
        }

        type = 0;
        local = -1;
        return false;
    }

    public static bool MatchArgOpcode(this Instruction ins, out OpcodeValueAction type, out int arg) {
        if (ins.MatchLdarg(out arg)) {
            type = OpcodeValueAction.Load;
            return true;
        }

        if (ins.MatchLdarga(out arg)) {
            type = OpcodeValueAction.LoadRef;
            return true;
        }

        if (ins.MatchStarg(out arg)) {
            type = OpcodeValueAction.Store;
            return true;
        }

        type = 0;
        arg = -1;
        return false;
    }
}

enum OpcodeValueAction {
    Load,
    LoadRef,
    Store
}