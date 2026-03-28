using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using MonoMod.Cil;
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

    public static void FillWithFakeILOffsets(this Mono.Collections.Generic.Collection<Instruction> collection) {
        for (int i = 0; i < collection.Count; i++)
        {
            collection[i].Offset = i;
        }
    }
}