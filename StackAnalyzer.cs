using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

public class StackAnalyzer
{
    private StackAnalyzer() { }

    static OpCode[] TwoParamMathOpcodes = [
        OpCodes.Add,
        OpCodes.Add_Ovf,
        OpCodes.Add_Ovf_Un,
        OpCodes.Sub,
        OpCodes.Sub_Ovf,
        OpCodes.Sub_Ovf_Un,
        OpCodes.Mul,
        OpCodes.Mul_Ovf,
        OpCodes.Mul_Ovf_Un,
        OpCodes.Div,
        OpCodes.Div_Un,
        OpCodes.Rem,
        OpCodes.Rem_Un,
        OpCodes.And,
        OpCodes.Or,
        OpCodes.Xor,
    ];

    static Dictionary<OpCode, Type> LdelemSimpleOpcodes = new() {
        { OpCodes.Ldelem_I,         typeof(nint)   },
        { OpCodes.Ldelem_I1,        typeof(sbyte)  },
        { OpCodes.Ldelem_I2,        typeof(short)  },
        { OpCodes.Ldelem_I4,        typeof(int)    },
        { OpCodes.Ldelem_I8,        typeof(long)   },
        { OpCodes.Ldelem_R4,        typeof(float)  },
        { OpCodes.Ldelem_R8,        typeof(double) },
        { OpCodes.Ldelem_U1,        typeof(byte)   },
        { OpCodes.Ldelem_U2,        typeof(ushort) },
        { OpCodes.Ldelem_U4,        typeof(uint)   },
    };

    static Dictionary<OpCode, Type> ConversionOpcodes = new() {
        { OpCodes.Conv_I,         typeof(nint)  },
        { OpCodes.Conv_I1,        typeof(sbyte) },
        { OpCodes.Conv_I2,        typeof(short) },
        { OpCodes.Conv_I4,        typeof(int)   },
        { OpCodes.Conv_I8,        typeof(long)  },
        { OpCodes.Conv_Ovf_I,     typeof(nint)  },
        { OpCodes.Conv_Ovf_I_Un,  typeof(nint)  },
        { OpCodes.Conv_Ovf_I1,    typeof(sbyte) },
        { OpCodes.Conv_Ovf_I1_Un, typeof(sbyte) },
        { OpCodes.Conv_Ovf_I2,    typeof(short) },
        { OpCodes.Conv_Ovf_I2_Un, typeof(short) },
        { OpCodes.Conv_Ovf_I4,    typeof(int)   },
        { OpCodes.Conv_Ovf_I4_Un, typeof(int)   },
        { OpCodes.Conv_Ovf_I8,    typeof(long)  },
        { OpCodes.Conv_Ovf_I8_Un, typeof(long)  },

        { OpCodes.Conv_U,         typeof(nuint)  },
        { OpCodes.Conv_U1,        typeof(byte)   },
        { OpCodes.Conv_U2,        typeof(ushort) },
        { OpCodes.Conv_U4,        typeof(uint)   },
        { OpCodes.Conv_U8,        typeof(ulong)  },
        { OpCodes.Conv_Ovf_U,     typeof(nuint)  },
        { OpCodes.Conv_Ovf_U_Un,  typeof(nuint)  },
        { OpCodes.Conv_Ovf_U1,    typeof(byte)   },
        { OpCodes.Conv_Ovf_U1_Un, typeof(byte)   },
        { OpCodes.Conv_Ovf_U2,    typeof(ushort) },
        { OpCodes.Conv_Ovf_U2_Un, typeof(ushort) },
        { OpCodes.Conv_Ovf_U4,    typeof(uint)   },
        { OpCodes.Conv_Ovf_U4_Un, typeof(uint)   },
        { OpCodes.Conv_Ovf_U8,    typeof(ulong)  },
        { OpCodes.Conv_Ovf_U8_Un, typeof(ulong)  },

        { OpCodes.Conv_R_Un,      typeof(float)  },
        { OpCodes.Conv_R4,        typeof(float)  },
        { OpCodes.Conv_R8,        typeof(double) },
    };



    static OpCode[] ComparisonOpcodes = [
        OpCodes.Ceq,  
        OpCodes.Cgt,  
        OpCodes.Cgt_Un,  
        OpCodes.Clt,  
        OpCodes.Clt_Un,  
    ];


    public static StackAnalysis Analyze(ILContext il)
    {
        List<bool> processedInstructions = new();
        List<bool> populatedInstructionInputs = new();
        List<InstructionStackInfo> infos = new();
        processedInstructions.Capacity = il.Instrs.Count;
        populatedInstructionInputs.Capacity = il.Instrs.Count;
        infos.Capacity = il.Instrs.Count;
        foreach (Instruction instr in il.Instrs)
        {
            processedInstructions.Add(false);
            populatedInstructionInputs.Add(false);
            infos.Add(new(instr));
        }

        Stack<int> indexesToProcess = new();

        for (int i = 0; i <= il.Instrs.Count; i++)
        {
            if (i >= il.Instrs.Count || processedInstructions[i])
            {
                if (indexesToProcess.Count == 0)
                {
                    break;
                }
                i = indexesToProcess.Pop() - 1;
                continue;
            }
            InstructionStackInfo info = infos[i];
            processedInstructions[i] = true;
            populatedInstructionInputs[i] = true;

            if (info.outValues.Count > 0)
                throw new InvalidProgramException("InstructionStackInfo.outValues populated before instruction was analyzed");

            info.outValues.AddRange(info.inValues);

            Instruction instr = info.instruction;
            OpCode opCode = instr.OpCode;
            switch (opCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    break;

                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Pop1:
                    if (info.outValues.Count == 0)
                        throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    break;

                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                case StackBehaviour.Pop1_pop1:
                    if (info.outValues.Count < 2)
                        throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    break;

                case StackBehaviour.Popref_popi_popi:
                    if (info.outValues.Count < 3)
                        throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                    info.outValues.RemoveAt(info.outValues.Count - 1);
                    break;

                case StackBehaviour.Varpop:
                    if (instr.MatchCallOrCallvirt(out MethodReference? method) || instr.MatchNewobj(out method))
                    {
                        if (string.IsNullOrEmpty(method.Name) && method.DeclaringType is null)
                        {
                            MethodBase m = method.ResolveReflection();

                            if (m is MethodInfo minfo)
                            {
                                if (!minfo.IsStatic && instr.OpCode != OpCodes.Newobj)
                                {
                                    if (info.outValues.Count == 0)
                                        throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                                    info.outValues.RemoveAt(info.outValues.Count - 1);
                                }
                                var ps = minfo.GetParameters();
                                for (int j = 0; j < ps.Length; j++)
                                {
                                    if (info.outValues.Count == 0)
                                        throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                                    info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                                    info.outValues.RemoveAt(info.outValues.Count - 1);
                                }
                                break;
                            }
                        }

                        if (method.HasThis && instr.OpCode != OpCodes.Newobj)
                        {
                            if (info.outValues.Count == 0)
                                throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                            info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                            info.outValues.RemoveAt(info.outValues.Count - 1);
                        }
                        for (int j = 0; j < method.Parameters.Count; j++)
                        {
                            if (info.outValues.Count == 0)
                                throw new InvalidProgramException($"Not enough values on the stack for {instr}");

                            info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                            info.outValues.RemoveAt(info.outValues.Count - 1);
                        }
                        break;
                    }
                    else if (instr.MatchRet())
                    {
                        if (info.outValues.Count >= 1)
                        {
                            info.outValues[info.outValues.Count - 1].consumedBy.Add(instr);
                            info.outValues.RemoveAt(info.outValues.Count - 1);
                        }
                        break;
                    }
                    Console.WriteLine($"Unsupported StackBehaviourPop {opCode.StackBehaviourPop} of opcode {opCode}");
                    Environment.Exit(-1);
                    break;

                default:
                    Console.WriteLine($"Unsupported StackBehaviourPop {opCode.StackBehaviourPop} of opcode {opCode}");
                    Environment.Exit(-1);
                    break;
            }

            switch (opCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    break;

                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    StackValue value;

                    if (instr.MatchLdsfld(out FieldReference? field) || instr.MatchLdfld(out field))
                    {
                        FieldInfo fieldInfo = field.ResolveReflection();
                        value = new(new StackValueOrigin.Field(fieldInfo), fieldInfo.FieldType, SimpleTypeFromSystemType(fieldInfo.FieldType));
                    }
                    else if (instr.MatchLdarg(out int arg) || instr.MatchLdarga(out arg))
                    {
                        ParameterDefinition? param = null;
                        if (instr.Operand is ParameterDefinition p)
                        {
                            param = p;
                        }
                        else if (arg >= 0 && il.Method.Parameters.Count > arg)
                        {
                            param = il.Method.Parameters[arg];
                        }
                        Type? type = param?.ParameterType.ResolveReflection();

                        if (instr.OpCode == OpCodes.Ldarga || instr.OpCode == OpCodes.Ldarga_S)
                        {
                            value = new(
                                new StackValueOrigin.Parameter(arg),
                                type?.MakeByRefType(),
                                SimpleType.Reference
                            );
                        }
                        else
                        {
                            value = new(
                                new StackValueOrigin.Parameter(arg),
                                type,
                                type is null ? SimpleType.Object : SimpleTypeFromSystemType(type)
                            );
                        }
                    }
                    else if (instr.MatchLdloc(out int loc) || instr.MatchLdloca(out loc))
                    {
                        VariableReference? variable = null;
                        if (instr.Operand is VariableReference v)
                        {
                            variable = v;
                        }
                        else if (loc >= 0 && il.Body.Variables.Count > loc)
                        {
                            variable = il.Body.Variables[loc];
                        }
                        Type? type = variable?.VariableType.ResolveReflection();

                        if (instr.OpCode == OpCodes.Ldloca || instr.OpCode == OpCodes.Ldloca_S)
                        {
                            value = new(
                                new StackValueOrigin.Local(loc),
                                type?.MakeByRefType(),
                                SimpleType.Reference
                            );
                        }
                        else
                        {
                            value = new(
                                new StackValueOrigin.Local(loc),
                                type,
                                type is null ? SimpleType.Object : SimpleTypeFromSystemType(type)
                            );
                        }
                    }
                    else if (TwoParamMathOpcodes.Contains(opCode))
                    {
                        StackValue inputVal1 = info.inValues[info.inValues.Count - 1];
                        StackValue inputVal2 = info.inValues[info.inValues.Count - 2];

                        Type? type = inputVal1.type;
                        SimpleType simpleType = inputVal1.simpleType;

                        if (type is null && inputVal2.type is not null)
                        {
                            type = inputVal2.type;
                            simpleType = inputVal2.simpleType;
                        }

                        value = new(
                            null,
                            type,
                            simpleType
                        );
                    }
                    else if (instr.MatchLdcI4(out int intValue))
                    {
                        value = new(new StackValueOrigin.ConstInt(intValue), typeof(int), SimpleType.Integer);
                    }
                    else if (instr.MatchLdcI8(out long longValue))
                    {
                        value = new(new StackValueOrigin.ConstLong(longValue), typeof(int), SimpleType.Integer);
                    }
                    else if (instr.MatchLdcR4(out float floatValue))
                    {
                        value = new(new StackValueOrigin.ConstFloat(floatValue), typeof(int), SimpleType.Integer);
                    }
                    else if (instr.MatchLdcR8(out double doubleValue))
                    {
                        value = new(new StackValueOrigin.ConstDouble(doubleValue), typeof(int), SimpleType.Integer);
                    }
                    else if (instr.MatchLdnull())
                    {
                        value = new(new StackValueOrigin.Null(), null, SimpleType.Object);
                    }
                    else if (instr.MatchNewarr(out TypeReference? arrayType))
                    {
                        Type type = arrayType.ResolveReflection().MakeArrayType();
                        value = new(null, type, SimpleType.Object);
                    }
                    else if (instr.MatchLdtoken(out IMetadataTokenProvider? token))
                    {
                        value = new(new StackValueOrigin.Token(token), null, SimpleType.Object);
                    }
                    else if (instr.MatchNewobj(out MethodReference? ctor))
                    {
                        value = new(null, ctor.DeclaringType.ResolveReflection(), SimpleType.Object);
                    }
                    else if (instr.MatchBox(out _))
                    {
                        value = new(null, typeof(object), SimpleType.Object);
                    }
                    else if (ConversionOpcodes.TryGetValue(opCode, out Type convertedType))
                    {
                        value = new(null, convertedType, SimpleTypeFromSystemType(convertedType));
                    }
                    else if (ComparisonOpcodes.Contains(opCode))
                    {
                        value = new(null, typeof(byte), SimpleType.Integer);
                    }
                    else if (LdelemSimpleOpcodes.TryGetValue(opCode, out Type valueType))
                    {
                        value = new(null, valueType, SimpleTypeFromSystemType(valueType));
                    }
                    else
                    {
                        Console.WriteLine($"Unsupported StackBehaviourPush {opCode.StackBehaviourPush} of opcode {opCode}");
                        Environment.Exit(-1);
                        break;
                    }

                    value.producedBy.Add(instr);

                    info.outValues.Add(value);

                    break;

                case StackBehaviour.Varpush:
                    if (instr.MatchCallOrCallvirt(out MethodReference? method))
                    {
                        MethodBase methodInfo = method.ResolveReflection();
                        Type? returnType = (methodInfo as MethodInfo)?.ReturnType;

                        if (returnType is not null && returnType != typeof(void))
                        {
                            value = new(
                                new StackValueOrigin.MethodCall(methodInfo),
                                returnType,
                                returnType is null ? SimpleType.Object : SimpleTypeFromSystemType(returnType)
                            );

                            value.producedBy.Add(instr);
                            info.outValues.Add(value);
                            break;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Unsupported StackBehaviourPush {opCode.StackBehaviourPush} of opcode {opCode}");
                        Environment.Exit(-1);
                        break;
                    }
                    break;

                case StackBehaviour.Push1_push1:

                    StackValue value1;
                    StackValue value2;

                    if (instr.MatchDup())
                    {
                        StackValue inputVal = info.inValues[info.inValues.Count - 1];

                        value1 = new(
                            null,
                            inputVal.type,
                            inputVal.simpleType
                        );
                        value2 = new(
                            null,
                            inputVal.type,
                            inputVal.simpleType
                        );
                    }
                    else
                    {
                        Console.WriteLine($"Unsupported StackBehaviourPush {opCode.StackBehaviourPush} of opcode {opCode}");
                        Environment.Exit(-1);
                        break;
                    }

                    value1.producedBy.Add(instr);
                    value2.producedBy.Add(instr);
                    info.outValues.Add(value1);
                    info.outValues.Add(value2);
                    break;

                default:
                    Console.WriteLine($"Unsupported StackBehaviourPush {opCode.StackBehaviourPush} of opcode {opCode}");
                    Environment.Exit(-1);
                    break;
            }

            if (opCode.FlowControl == FlowControl.Return)
            {
                if (indexesToProcess.Count == 0)
                {
                    break;
                }
                i = indexesToProcess.Pop() - 1;
                continue;
            }

            switch (opCode.FlowControl)
            {
                case FlowControl.Branch:
                    Instruction? nextInstr = null;

                    if (instr.Operand is Instruction ins1)
                        nextInstr = ins1;

                    else if (instr.Operand is ILLabel label1)
                        nextInstr = label1.Target!;

                    if (nextInstr is null)
                        throw new InvalidProgramException($"Operand of {opCode} at IL_{instr.Offset:x4} wasn't pointing to an instruction");

                    int index = il.Instrs.IndexOf(nextInstr);

                    if (index < 0)
                        throw new InvalidProgramException($"Operand of {opCode} at IL_{instr.Offset:x4} wasn't pointing to an instruction");

                    if (populatedInstructionInputs[index])
                    {
                        MergeStackValues(infos[index].inValues, info.outValues, infos, nextInstr);
                    }
                    else
                    {
                        if (infos[index].inValues.Count > 0)
                            throw new InvalidProgramException("InstructionStackInfo.inValues populated at the wrong time");

                        infos[index].inValues.AddRange(info.outValues);
                        populatedInstructionInputs[index] = true;
                    }

                    i = index - 1;
                    continue;

                case FlowControl.Call:
                case FlowControl.Next:
                    if (populatedInstructionInputs[i + 1])
                    {
                        MergeStackValues(infos[i + 1].inValues, info.outValues, infos, infos[i+1].instruction);
                    }
                    else
                    {
                        if (infos[i + 1].inValues.Count > 0)
                            throw new InvalidProgramException("InstructionStackInfo.inValues populated at the wrong time");

                        infos[i + 1].inValues.AddRange(info.outValues);
                        populatedInstructionInputs[i + 1] = true;
                    }
                    break;

                case FlowControl.Cond_Branch:

                    if (populatedInstructionInputs[i + 1])
                    {
                        MergeStackValues(infos[i + 1].inValues, info.outValues, infos, infos[i+1].instruction);
                    }
                    else
                    {
                        if (infos[i + 1].inValues.Count > 0)
                            throw new InvalidProgramException("InstructionStackInfo.inValues populated at the wrong time");

                        infos[i + 1].inValues.AddRange(info.outValues);
                        populatedInstructionInputs[i + 1] = true;
                    }


                    IEnumerable<Instruction>? nextInstrs = null;

                    if (instr.Operand is Instruction ins)
                        nextInstrs = [ins];

                    else if (instr.Operand is ILLabel label)
                        nextInstrs = [label.Target!];

                    if (instr.Operand is Instruction[] inss)
                        nextInstrs = inss;

                    else if (instr.Operand is ILLabel[] labels)
                        nextInstrs = labels.Select(l => l.Target!);

                    if (nextInstrs is not null)
                    {
                        foreach (Instruction nextInstr1 in nextInstrs)
                        {
                            index = -1;
                            for (int i1 = 0; i1 < infos.Count; i1++)
                            {
                                if (infos[i1].instruction == nextInstr1)
                                {
                                    index = i1;
                                    break;
                                }
                            }

                            if (index < 0)
                                throw new InvalidProgramException($"Instruction {i} at IL_{instr.Offset:x4} points to invalid label");

                            if (populatedInstructionInputs[index])
                            {
                                MergeStackValues(infos[index].inValues, info.outValues, infos, infos[index].instruction);
                            }
                            else
                            {
                                if (infos[index].inValues.Count > 0)
                                    throw new InvalidProgramException("InstructionStackInfo.inValues populated at the wrong time");

                                infos[index].inValues.AddRange(info.outValues);
                                populatedInstructionInputs[index] = true;
                            }

                            if (!processedInstructions[index])
                            {
                                indexesToProcess.Push(index);
                            }
                        }

                        break;
                    }

                    Console.WriteLine($"Unsupported FlowControl {opCode.FlowControl} of opcode {opCode}");
                    Environment.Exit(-1);
                    break;

                default:
                    Console.WriteLine($"Unsupported FlowControl {opCode.FlowControl} of opcode {opCode}");
                    Environment.Exit(-1);
                    break;
            }
        }

        return new(infos);
    }

    static void MergeStackValues(List<StackValue> into, List<StackValue> from, List<InstructionStackInfo> infos, Instruction at)
    {
        if (into.Count != from.Count)
            Console.WriteLine($"Merging two stacks of different size {from.Count} -> {into.Count} at IL_{at.Offset:x4}");

        for (int i = 0; i < Math.Min(into.Count, from.Count); i++)
        {
            StackValue fromv = from[i];
            StackValue intov = into[i];

            if (intov.type is null)
            {
                intov.type = fromv.type;
                intov.simpleType = fromv.simpleType;
            }

            intov.origin = null;

            foreach (Instruction instr in fromv.producedBy)
                if (!intov.producedBy.Contains(instr))
                    intov.producedBy.Add(instr);

            foreach (Instruction instr in fromv.consumedBy)
                if (!intov.consumedBy.Contains(instr))
                    intov.consumedBy.Add(instr);

            foreach (InstructionStackInfo info in infos)
            {
                for (int j = 0; j < info.inValues.Count; j++)
                {
                    if (ReferenceEquals(info.inValues[j], fromv))
                    {
                        info.inValues[j] = intov;
                    }
                }

                for (int j = 0; j < info.outValues.Count; j++)
                {
                    if (ReferenceEquals(info.outValues[j], fromv))
                    {
                        info.outValues[j] = intov;
                    }
                }
            }
        }
    }

    static SimpleType SimpleTypeFromSystemType(Type type)
    {
        if (type.IsByRef)
        {
            return SimpleType.Reference;
        }
        else if (type == typeof(float))
        {
            return SimpleType.Float;
        }
        else if (type == typeof(double))
        {
            return SimpleType.Double;
        }
        else if (type.IsPrimitive)
        {
            if (type.GetManagedSize() < 8)
                return SimpleType.Integer;
            else if (type == typeof(long))
                return SimpleType.Long;
            else
                throw new Exception($"What's that pokemon? It's {type.FullName}");
        }
        else
        {
            return SimpleType.Object;
        }
    }
}

public class StackAnalysis
{
    public List<InstructionStackInfo> instructions;

    public StackAnalysis(List<InstructionStackInfo> instructions)
    {
        this.instructions = instructions;
    }

    public InstructionStackInfo? LookupInstruction(Instruction instr, out int index)
    {
        index = -1;

        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].instruction == instr)
            {
                index = i;
                return instructions[i];
            }
        }

        return null;
    }
}

public class InstructionStackInfo
{
    public readonly Instruction instruction;

    public List<StackValue> inValues = new();

    public List<StackValue> outValues = new();

    public InstructionStackInfo(Instruction instruction)
    {
        this.instruction = instruction;
    }
}

public enum SimpleType
{
    Integer,
    Long,
    Float,
    Double,
    Reference,
    Object,
}

public class StackValue
{
    public List<Instruction> producedBy = new();
    public List<Instruction> consumedBy = new();

    public SimpleType simpleType;
    public Type? type;
    public StackValueOrigin? origin;

    public StackValue(StackValueOrigin? origin, Type? type, SimpleType simpleType)
    {
        this.origin = origin;
        this.type = type;
        this.simpleType = simpleType;
    }
}

public record StackValueOrigin
{
    private StackValueOrigin() { }

    public record Field(FieldInfo Info) : StackValueOrigin;
    public record MethodCall(MethodBase Method) : StackValueOrigin;
    public record Parameter(int Param) : StackValueOrigin;
    public record Local(int Index) : StackValueOrigin;
    public record ConstInt(int Value) : StackValueOrigin;
    public record ConstLong(long Value) : StackValueOrigin;
    public record ConstFloat(float Value) : StackValueOrigin;
    public record ConstDouble(double Value) : StackValueOrigin;
    public record Token(IMetadataTokenProvider TokenProvider) : StackValueOrigin;
    public record Null() : StackValueOrigin;
}