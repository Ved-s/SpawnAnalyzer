using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;

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

    public static void FancyPrintout(this ILContext il)
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

        for (int i = 0; i < il.Instrs.Count; i++)
        {
            Console.Write(DnSpyAnsiColors.comment);
            Console.Write("/* ");
            Console.Write(i);
            int indexWidth = (i == 0) ? 1 : (int)Math.Log10(i) + 1;
            for (int j = 0; j < (maxIndexWidth - indexWidth); j++)
                Console.Write(" ");
            Console.Write(" */ ");
            AssemblyPrint.Print(il.Instrs[i]);
            Console.WriteLine();
            if (il.Instrs[i].Operand is ILLabel or Instruction or ILLabel[] or Instruction[])
            {
                Console.WriteLine();
            }
        }
        Console.Write(DnSpyAnsiColors.reset);

    }
}