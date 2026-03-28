using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer;

namespace SpawnAnalyzer;

public static class CallInliner
{
    public static void InlineAllCalls(ILContext il, Func<MethodReference, bool> matcher)
    {
        StackAnalysis stack = StackAnalyzer.Analyze(il);

        var instructions = il.Body.Instructions;

        List<VariableDefinition> newLocals = new();

        for (int i = 0; i < instructions.Count; i++)
        {
            Instruction instr = instructions[i];
            if (instr.OpCode != OpCodes.Call)
                continue;

            MethodReference method = instr.Operand as MethodReference
                ?? throw new InvalidProgramException("call with a non-method operand");

            if (!matcher(method))
                continue;


            MethodInfo resolved = method.ResolveReflection() as MethodInfo
                ?? throw new MissingMethodException($"Could not find methodinfo for {method.FullName} for inlining");

            InstructionStackInfo stackinfo = stack.LookupInstruction(instr, out _) ?? throw new Exception("what");

            ParameterInfo[] parameters = resolved.GetParameters();
            int paramCount = parameters.Length;

            if (!resolved.IsStatic)
                paramCount++;

            StackValue[] paramValues = new StackValue[paramCount];
            int start = stackinfo.inValues.Count - paramCount;
            for (int j = 0; j < paramCount; j++)
            {
                paramValues[j] = stackinfo.inValues[start + j];
            }

            InlineParameter[] inlineParams = new InlineParameter[paramCount];

            for (int j = 0; j < paramCount; j++)
            {
                StackValue value = paramValues[j];

                bool removeSource = false;

                InlineParameter? param = null!;

                if (value.producedBy.Count == 1)
                {
                    Instruction producer = value.producedBy[0];
                    if (producer.MatchLdnull())
                    {
                        param = new InlineParameter.Null();
                        removeSource = true;
                    }
                    else if (producer.MatchLdcI4(out int intval))
                    {
                        param = new InlineParameter.ConstInt(intval);
                        removeSource = true;
                    }
                    else if (producer.MatchLdcI8(out long longval))
                    {
                        param = new InlineParameter.ConstLong(longval);
                        removeSource = true;
                    }
                    else if (producer.MatchLdcR4(out float floatval))
                    {
                        param = new InlineParameter.ConstFloat(floatval);
                        removeSource = true;
                    }
                    else if (producer.MatchLdcR8(out double doubleval))
                    {
                        param = new InlineParameter.ConstDouble(doubleval);
                        removeSource = true;
                    }
                    else if (producer.MatchLdarg(out int index))
                    {
                        param = new InlineParameter.Argument(index);
                        removeSource = true;
                    }
                    else if (producer.MatchLdarga(out index))
                    {
                        param = new InlineParameter.ArgumentRef(index);
                        removeSource = true;
                    }
                    else if (producer.MatchLdloc(out index))
                    {
                        param = new InlineParameter.Local(index);
                        removeSource = true;
                    }
                    else if (producer.MatchLdloca(out index))
                    {
                        param = new InlineParameter.LocalRef(index);
                        removeSource = true;
                    }

                    if (removeSource)
                    {
                        il.RetargetLabels(producer, producer.Next);
                        instructions.Remove(producer);
                        i--;
                    }
                }

                if (param is null)
                {
                    SpawnAnalyzer.ReportUnknownPattern($"param {j} source for inlining", il, i, 10, 5);
                    throw new InvalidOperationException($"Unsupported param {j} source for inlining at IL_{instr.Offset:x4}");
                }
                else
                {
                    inlineParams[j] = param;
                }
            }

            i += InlineCall(il, i + 1, resolved, inlineParams, newLocals);

            il.RetargetLabels(instr, instr.Next);
            instructions.Remove(instr);
        }
    }

    /// <summary>
    /// Inline the method into current ILContext starting at start offset
    /// </summary>
    public static int InlineCall(ILContext il, int start, MethodInfo method, InlineParameter[] inlineParams, List<VariableDefinition>? freeLocals = null)
    {
        return InlineMethodDefinition(il, start, new DynamicMethodDefinition(method).Definition, inlineParams, freeLocals);
    }

    /// <summary>
    /// Inline the method into current ILContext starting at start offset
    /// </summary>
    public static int InlineMethodDefinition(ILContext il, int start, MethodDefinition method, InlineParameter[] inlineParams, List<VariableDefinition>? freeLocals = null)
    {
        List<Instruction> newInstructions = method.Body.Instructions.ToList();
        method.Body.Instructions.Clear();
        return InlineMethodBody(il, start, method.Body.Variables, newInstructions, inlineParams, freeLocals);
    }

    /// <summary>
    /// Inline the method into current ILContext starting at start offset
    /// </summary>
    public static int InlineMethodBody(ILContext il, int start, IEnumerable<VariableDefinition> locals, IEnumerable<Instruction> body, InlineParameter[] inlineParams, List<VariableDefinition>? freeLocals = null, bool allowStarg = false)
    {
        List<VariableDefinition> newLocalsPicker = new();

        List<VariableDefinition> localsRemap = new();
        Dictionary<Instruction, ILLabel> instructionLabelMap = new();

        ILLabel returnLabel = il.DefineLabel();
        returnLabel.Target = il.Instrs[start];

        if (freeLocals is not null)
            newLocalsPicker.AddRange(freeLocals);

        foreach (VariableDefinition local in locals)
        {
            int existingLocal = newLocalsPicker.FindIndex(l => l.VariableType.FullName == local.VariableType.FullName);

            VariableDefinition pickedLocal;

            if (existingLocal < 0)
            {
                pickedLocal = new(local.VariableType);
                freeLocals?.Add(pickedLocal);
                il.Body.Variables.Add(pickedLocal);
            }
            else
            {
                pickedLocal = newLocalsPicker[existingLocal];
                newLocalsPicker.RemoveAt(existingLocal);
            }

            localsRemap.Add(pickedLocal);
        }

        instructionLabelMap.Clear();

        foreach (Instruction newInstr in body)
        {
            ILLabel ProcessInstruction(Instruction instr)
            {
                if (!instructionLabelMap.TryGetValue(instr, out ILLabel? label))
                {
                    label = il.DefineLabel();
                    label.Target = instr;
                    instructionLabelMap[instr] = label;
                }
                return label;
            }
            switch (newInstr.Operand)
            {
                case Instruction instr:
                    newInstr.Operand = ProcessInstruction(instr);
                    break;

                case Instruction[] instrs:
                    ILLabel[] labels = new ILLabel[instrs.Length];
                    for (int j = 0; j < instrs.Length; j++)
                    {
                        labels[j] = ProcessInstruction(instrs[j]);
                    }
                    newInstr.Operand = labels;
                    break;

                case ILLabel l:
                    newInstr.Operand = ProcessInstruction(l.Target!);
                    break;

                case ILLabel[] ls:
                    labels = new ILLabel[ls.Length];
                    for (int j = 0; j < ls.Length; j++)
                    {
                        labels[j] = ProcessInstruction(ls[j].Target!);
                    }
                    newInstr.Operand = labels;
                    break;
            }
        }

        var instructions = il.Instrs;

        int i = start;

        foreach (Instruction newInstr in body)
        {
            if (newInstr.OpCode == OpCodes.Ret)
            {
                newInstr.OpCode = OpCodes.Br;
                newInstr.Operand = returnLabel;
            }
            else if (newInstr.MatchLdloc(out int index))
            {
                newInstr.OpCode = OpCodes.Ldloc;
                newInstr.Operand = localsRemap[index];
            }
            else if (newInstr.MatchLdloca(out index))
            {
                newInstr.OpCode = OpCodes.Ldloca;
                newInstr.Operand = localsRemap[index];
            }
            else if (newInstr.MatchStloc(out index))
            {
                newInstr.OpCode = OpCodes.Stloc;
                newInstr.Operand = localsRemap[index];
            }
            else if (newInstr.MatchStarg(out index))
            {
                if (allowStarg)
                {
                    InlineParameter param = inlineParams[index];
                    switch (param)
                    {
                        case InlineParameter.Argument arg:
                            newInstr.OpCode = OpCodes.Starg;
                            newInstr.Operand = il.Method.Parameters[arg.Index];
                            break;

                        case InlineParameter.Local loc:
                            newInstr.OpCode = OpCodes.Stloc;
                            newInstr.Operand = il.Body.Variables[loc.Index];
                            break;

                        default:
                            throw new InvalidOperationException($"Invalid InlineParameter.{param.GetType().Name} for starg");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unsupported starg in inlining function");
                }
            }
            else if (newInstr.MatchLdarga(out _))
            {
                throw new InvalidOperationException("Unsupported ldarga in inlining function");
            }
            else if (newInstr.MatchLdarg(out index))
            {
                InlineParameter param = inlineParams[index];
                switch (param)
                {
                    case InlineParameter.Null:
                        newInstr.OpCode = OpCodes.Ldnull;
                        newInstr.Operand = null;
                        break;

                    case InlineParameter.ConstInt ci:
                        newInstr.OpCode = OpCodes.Ldc_I4;
                        newInstr.Operand = ci.Int;
                        break;

                    case InlineParameter.ConstLong cl:
                        newInstr.OpCode = OpCodes.Ldc_I8;
                        newInstr.Operand = cl.Long;
                        break;

                    case InlineParameter.ConstFloat cf:
                        newInstr.OpCode = OpCodes.Ldc_R4;
                        newInstr.Operand = cf.Float;
                        break;

                    case InlineParameter.ConstDouble cd:
                        newInstr.OpCode = OpCodes.Ldc_R8;
                        newInstr.Operand = cd.Double;
                        break;

                    case InlineParameter.Argument arg:
                        newInstr.OpCode = OpCodes.Ldarg;
                        newInstr.Operand = il.Method.Parameters[arg.Index];
                        break;

                    case InlineParameter.ArgumentRef argref:
                        newInstr.OpCode = OpCodes.Ldarga;
                        newInstr.Operand = il.Method.Parameters[argref.Index];
                        break;

                    case InlineParameter.Local loc:
                        newInstr.OpCode = OpCodes.Ldloc;
                        newInstr.Operand = il.Body.Variables[loc.Index];
                        break;

                    case InlineParameter.LocalRef locref:
                        newInstr.OpCode = OpCodes.Ldloca;
                        newInstr.Operand = il.Body.Variables[locref.Index];
                        break;

                    default:
                        throw new NotImplementedException($"Unhandled InlineParameter.{param.GetType().Name}");
                }
            }

            instructions.Insert(i, newInstr);
            i++;
        }

        return i - start;
    }
}

public record class InlineParameter
{
    public record class Null() : InlineParameter;
    public record class ConstInt(int Int) : InlineParameter;
    public record class ConstLong(long Long) : InlineParameter;
    public record class ConstFloat(float Float) : InlineParameter;
    public record class ConstDouble(double Double) : InlineParameter;
    public record class Argument(int Index) : InlineParameter;
    public record class ArgumentRef(int Index) : InlineParameter;
    public record class Local(int Index) : InlineParameter;
    public record class LocalRef(int Index) : InlineParameter;
}