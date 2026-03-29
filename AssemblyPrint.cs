using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria.GameContent.LeashedEntities;

namespace SpawnAnalyzer;

static class AssemblyPrint
{
    public static void Print(Instruction instr, bool tryResolve = true)
    {
        Console.Write(DnSpyAnsiColors.label);
        Console.Write("IL_");
        Console.Write(instr.Offset.ToString("x4"));
        Console.Write(DnSpyAnsiColors.defaulttext);
        Console.Write(": ");

        Console.Write(DnSpyAnsiColors.opcode);
        Console.Write(instr.OpCode.Name);

        if (instr.Operand is null)
        {
            Console.Write(DnSpyAnsiColors.reset);
            return;
        }

        int remLen = 9 - instr.OpCode.Name.Length;
        for (int i = 0; i < remLen; i++)
            Console.Write(" ");

        Console.Write(" ");

        switch (instr.Operand)
        {
            case ILLabel label:
                Print(label);
                break;

            case Instruction instrp:
                Console.Write(DnSpyAnsiColors.label);
                Console.Write("IL_");
                Console.Write(instrp.Offset.ToString("x4"));
                break;

            case Instruction[] instrs:
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write("(");
                for (int i = 0; i < instrs.Length; i++)
                {
                    if (i > 0)
                    {
                        Console.Write(DnSpyAnsiColors.punctuation);
                        Console.Write(", ");
                    }
                    Console.Write(DnSpyAnsiColors.label);
                    Console.Write("IL_");
                    Console.Write(instrs[i].Offset.ToString("x4"));
                }
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write(")");
                break;

            case ILLabel[] labels:
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write("(");
                for (int i = 0; i < labels.Length; i++)
                {
                    if (i > 0)
                    {
                        Console.Write(DnSpyAnsiColors.punctuation);
                        Console.Write(", ");
                    }
                    Console.Write(DnSpyAnsiColors.label);
                    Console.Write("IL_");
                    Console.Write(labels[i].Target!.Offset.ToString("x4"));
                }
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write(")");
                break;

            case FieldReference field:
                if (tryResolve)
                {
                    FieldInfo? resolvedField = null;
                    try
                    {
                        resolvedField = field.ResolveReflection();
                    }
                    catch { }
                    if (resolvedField is null)
                    {
                        Console.Write(DnSpyAnsiColors.comment);
                        Console.Write("/* unresolved */ ");
                        Print(field);
                    }
                    else
                    {
                        Print(resolvedField);
                    }
                }
                else
                {
                    Print(field);
                }
                break;

            case MethodReference method:
                if (tryResolve)
                {
                    MethodInfo? resolvedMethod = null;
                    try
                    {
                        resolvedMethod = method.ResolveReflection() as MethodInfo;
                    }
                    catch { }
                    if (resolvedMethod is null)
                    {
                        Console.Write(DnSpyAnsiColors.comment);
                        Console.Write("/* unresolved */ ");
                        Print(method);
                    }
                    else
                    {
                        Print(resolvedMethod);
                    }
                }
                else
                {
                    Print(method);
                }
                break;

            case TypeReference type:
                if (tryResolve)
                {
                    Type? resolvedType = null;
                    try
                    {
                        resolvedType = type.ResolveReflection();
                    }
                    catch { }
                    if (resolvedType is null)
                    {
                        Console.Write(DnSpyAnsiColors.comment);
                        Console.Write("/* unresolved */ ");
                        Print(type);
                    }
                    else
                    {
                        Print(resolvedType);
                    }
                }
                else
                {
                    Print(type);
                }
                break;

            case ParameterDefinition param:
                Console.Write(DnSpyAnsiColors.parameter);
                Console.Write(param.Name);
                Console.Write(DnSpyAnsiColors.reset);
                break;

            case VariableDefinition var:

                Console.Write(DnSpyAnsiColors.local);
                Console.Write("V_");
                if (var.Index < 0)
                    Console.Write("Err");
                else
                    Console.Write(var.Index);
                break;

            case float f:
                Console.Write(DnSpyAnsiColors.number);
                Console.Write(f);
                break;

            case double f:
                Console.Write(DnSpyAnsiColors.number);
                Console.Write(f);
                break;

            case sbyte i:
                Console.Write(DnSpyAnsiColors.number);
                Console.Write(i);
                break;

            case int i:
                Console.Write(DnSpyAnsiColors.number);
                Console.Write(i);
                break;

            default:
                Console.Write(DnSpyAnsiColors.comment);
                Console.Write("// unsupported operand (");
                Console.Write(instr.Operand.GetType());
                Console.Write("): ");
                Console.Write(instr.Operand);
                break;
        }

        Console.Write(DnSpyAnsiColors.reset);
    }

    public static void Print(ILLabel label)
    {
        Console.Write(DnSpyAnsiColors.label);
        Console.Write("IL_");
        Console.Write(label.Target!.Offset.ToString("x4"));
    }

    public static void Print(FieldReference field)
    {
        Print(field.FieldType);
        Console.Write(" ");
        Print(field.DeclaringType, false);
        Console.Write(DnSpyAnsiColors.punctuation);
        Console.Write("::");
        if (field.ResolveReflection()?.IsStatic ?? false)
            Console.Write(DnSpyAnsiColors.staticfield);
        else
            Console.Write(DnSpyAnsiColors.instancefield);
        Console.Write(field.Name);
    }

    public static void Print(FieldInfo field)
    {
        Print(field.FieldType);
        Console.Write(" ");
        if (field.DeclaringType is not null)
        {
            Print(field.DeclaringType, false);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("::");
        }
        if (field.IsStatic)
            Console.Write(DnSpyAnsiColors.staticfield);
        else
            Console.Write(DnSpyAnsiColors.instancefield);
        Console.Write(field.Name);
    }

    public static void Print(TypeReference type, bool withPrefix = true)
    {
        string? aliasName = null;

        if (type.IsPrimitive && type.Namespace == "System" || type.FullName == "System.Void" || type.FullName == "System.Object")
        {
            aliasName = type.Name switch
            {
                nameof(Object) => "object",
                nameof(Boolean) => "bool",
                "Void" => "void",
                nameof(Single) => "float32",
                nameof(Double) => "float64",
                nameof(Byte) => "uint8",
                nameof(SByte) => "int8",
                nameof(UInt16) => "uint16",
                nameof(Int16) => "int16",
                nameof(UInt32) => "uint32",
                nameof(Int32) => "int32",
                nameof(UInt64) => "uint64",
                nameof(Int64) => "int64",
                nameof(UIntPtr) => "nuint",
                nameof(IntPtr) => "nint",
                _ => null,
            };
        }

        if (type.IsNested)
        {
            Print(type.DeclaringType, withPrefix);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("/");
        }
        else
        {
            if (withPrefix && aliasName is null)
            {
                Console.Write(DnSpyAnsiColors.keyword);
                if (type.IsValueType)
                {
                    Console.Write("valuetype ");
                }
                else
                {
                    Console.Write("class ");
                }
            }

            if (aliasName is null)
            {
                if (type.Namespace != "")
                {
                    foreach (string ns in type.Namespace.Split('.'))
                    {
                        Console.Write(DnSpyAnsiColors.@namespace);
                        Console.Write(ns);
                        Console.Write(DnSpyAnsiColors.punctuation);
                        Console.Write(".");
                    }
                }
            }
        }

        if (aliasName is not null)
        {
            Console.Write(DnSpyAnsiColors.keyword);
            Console.Write(aliasName);
        }
        else
        {

            bool staticClass = false;
            try
            {
                staticClass = IsStaticClass(type.ResolveReflection());
            }
            catch { }

            if (type.IsValueType)
                Console.Write(DnSpyAnsiColors.valuetype);
            else if (staticClass)
                Console.Write(DnSpyAnsiColors.statictype);
            else
                Console.Write(DnSpyAnsiColors.type);

            Console.Write(type.Name.TrimEnd('&'));
        }

        if (type.IsByReference)
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("&");
        }
    }

    public static void Print(Type type, bool withPrefix = true)
    {
        if (type.IsArray)
        {
            Print(type.GetElementType()!);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("[");
            int rank = type.GetArrayRank();
            for (int i = 1; i < rank; i++)
            {
                Console.Write(",");
            }
            Console.Write("]");
            return;
        }

        if (type.IsByRef)
        {
            Print(type.GetElementType()!);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("&");
            return;
        }

        string? aliasName = null;

        if (type.IsPrimitive && type.Namespace == "System" || type.FullName == "System.Void" || type.FullName == "System.Object")
        {
            aliasName = type.Name switch
            {
                nameof(Object) => "object",
                nameof(Boolean) => "bool",
                "Void" => "void",
                nameof(Single) => "float32",
                nameof(Double) => "float64",
                nameof(Byte) => "uint8",
                nameof(SByte) => "int8",
                nameof(UInt16) => "uint16",
                nameof(Int16) => "int16",
                nameof(UInt32) => "uint32",
                nameof(Int32) => "int32",
                nameof(UInt64) => "uint64",
                nameof(Int64) => "int64",
                nameof(UIntPtr) => "nuint",
                nameof(IntPtr) => "nint",
                _ => null,
            };
        }

        if (type.IsNested)
        {
            Print(type.DeclaringType!, withPrefix);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("/");
        }
        else
        {
            if (withPrefix && aliasName is null)
            {
                Console.Write(DnSpyAnsiColors.keyword);
                if (type.IsValueType)
                {
                    Console.Write("valuetype ");
                }
                else
                {
                    Console.Write("class ");
                }
            }

            if (aliasName is null)
            {
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    foreach (string ns in type.Namespace.Split('.'))
                    {
                        Console.Write(DnSpyAnsiColors.@namespace);
                        Console.Write(ns);
                        Console.Write(DnSpyAnsiColors.punctuation);
                        Console.Write(".");
                    }
                }
            }
        }

        if (aliasName is not null)
        {
            Console.Write(DnSpyAnsiColors.keyword);
            Console.Write(aliasName);
        }
        else
        {
            bool staticClass = IsStaticClass(type);

            if (type.IsValueType)
                Console.Write(DnSpyAnsiColors.valuetype);
            else if (staticClass)
                Console.Write(DnSpyAnsiColors.statictype);
            else
                Console.Write(DnSpyAnsiColors.type);

            string name = type.Name;

            if (type.IsGenericType)
            {
                int generics = type.GetGenericArguments().Length;
                string genericSuffix = $"`{generics}";

                if (name.EndsWith(genericSuffix))
                {
                    name = name.Substring(0, name.Length - genericSuffix.Length);
                }
            }

            Console.Write(name);
        }

        if (type.IsGenericType)
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("<");

            var generics = type.GetGenericArguments();
            for (int i = 0; i < generics.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(DnSpyAnsiColors.punctuation);
                    Console.Write(", ");
                }
                Print(generics[i]);
            }

            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write(">");
        }
    }

    public static void Print(MethodReference method)
    {
        if (method.HasThis && !method.ExplicitThis)
        {
            Console.Write(DnSpyAnsiColors.keyword);
            Console.Write("instance ");
        }

        Print(method.ReturnType);
        Console.Write(" ");
        if (method.DeclaringType is not null)
        {
            Print(method.DeclaringType, false);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("::");
        }
        if (method.ExplicitThis)
            Console.Write(DnSpyAnsiColors.extensionmethod);
        else if (method.HasThis)
            Console.Write(DnSpyAnsiColors.instancemethod);
        else
            Console.Write(DnSpyAnsiColors.staticmethod);
        Console.Write(method.Name);
        Console.Write(DnSpyAnsiColors.reset);
        if (method.GenericParameters.Count > 0)
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("<");

            for (int i = 0; i < method.GenericParameters.Count; i++)
            {
                if (i > 0)
                {
                    Console.Write(DnSpyAnsiColors.punctuation);
                    Console.Write(", ");
                }
                Print(method.GenericParameters[i]);
            }

            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write(">");

        }
        Console.Write(DnSpyAnsiColors.punctuation);
        Console.Write("(");

        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (i > 0)
            {
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write(", ");
            }
            Print(method.Parameters[i].ParameterType);
        }

        Console.Write(DnSpyAnsiColors.punctuation);
        Console.Write(")");
    }

    public static void Print(MethodInfo method)
    {
        if (!method.IsStatic)
        {
            Console.Write(DnSpyAnsiColors.keyword);
            Console.Write("instance ");
        }

        Print(method.ReturnType);
        Console.Write(" ");
        if (method.DeclaringType is not null)
        {
            Print(method.DeclaringType, false);
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("::");
        }
        if (method.IsStatic)
            Console.Write(DnSpyAnsiColors.staticmethod);
        else
            Console.Write(DnSpyAnsiColors.instancemethod);
        Console.Write(method.Name);
        Console.Write(DnSpyAnsiColors.reset);
        if (method.IsGenericMethod)
        {
            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write("<");

            var generics = method.GetGenericArguments();
            for (int i = 0; i < generics.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(DnSpyAnsiColors.punctuation);
                    Console.Write(", ");
                }
                Print(generics[i]);
            }

            Console.Write(DnSpyAnsiColors.punctuation);
            Console.Write(">");

        }
        Console.Write(DnSpyAnsiColors.punctuation);
        Console.Write("(");

        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                Console.Write(DnSpyAnsiColors.punctuation);
                Console.Write(", ");
            }
            Print(parameters[i].ParameterType);
        }

        Console.Write(DnSpyAnsiColors.punctuation);
        Console.Write(")");
    }

    static bool IsStaticClass(Type type)
    {
        return !type.IsValueType && !type.IsPrimitive && type.IsAbstract && type.IsSealed;
    }
}