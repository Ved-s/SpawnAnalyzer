using System;
using System.Reflection;
using System.Text;

namespace SpawnAnalyzer;

public static class Utils
{
    public static MethodInfo GetMethodOrThrow(this Type type, string name)
    {
        return type.GetMethod(name, (BindingFlags)(-1)) ?? throw new MissingMethodException($"{type.FullName}::{name}");
    }

    public static MethodInfo GetMethodOrThrow(this Type type, string name, Type[] args)
    {
        MethodInfo? m = type.GetMethod(name, (BindingFlags)(-1), null, args, null);
        if (m is not null)
            return m;

        StringBuilder sb = new();
        sb.Append(type.FullName);
        sb.Append("::");
        sb.Append(name);
        sb.Append('(');
        
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(args[i].FullName);
        }
        sb.Append(')');
        throw new MissingMethodException(sb.ToString());
    }

    public static PropertyInfo GetPropertyOrThrow(this Type type, string name)
    {
        return type.GetProperty(name, (BindingFlags)(-1)) ?? throw new MissingMemberException($"property {type.FullName}::{name}");
    }

    public static FieldInfo GetFieldOrThrow(this Type type, string name)
    {
        return type.GetField(name, (BindingFlags)(-1)) ?? throw new MissingFieldException($"{type.FullName}::{name}");
    }

    public static MethodInfo GetMethodOrThrow<T>(string name)
    {
        return GetMethodOrThrow(typeof(T), name);
    }

    public static MethodInfo GetMethodOrThrow<T>(string name, Type[] args)
    {
        return GetMethodOrThrow(typeof(T), name, args);
    }

    public static PropertyInfo GetPropertyOrThrow<T>(string name)
    {
        return GetPropertyOrThrow(typeof(T), name);
    }

    public static FieldInfo GetFieldOrThrow<T>(string name)
    {
        return GetFieldOrThrow(typeof(T), name);
    }
}