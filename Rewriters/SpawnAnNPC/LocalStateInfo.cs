using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using SpawnAnalyzer.Simulation;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace SpawnAnalyzer.Rewriters.SpawnANnNPC;

public class LocalStateInfo
{
    static int GeneratedCount = 0;

    public Type Type;

    public new Func<object, object, bool> Equals;
    public Func<object, object> Clone;

    public LocalStateInfo(Type type, Func<object, object, bool> equals, Func<object, object> clone)
    {
        Type = type;
        Equals = equals;
        Clone = clone;
    }

    public static LocalStateInfo RewriteLocalState(ILContext il, ParameterDefinition contextParam)
    {
        TypeBuilder typeBuilder = SpawnAnNPCRewriter.TypeModule.DefineType($"MethodLocalStateStore_{GeneratedCount}");
        GeneratedCount++;

        foreach (VariableDefinition var in il.Body.Variables)
        {
            if (var.Index < 0)
                continue;

            Type vartype = var.VariableType.ResolveReflection();

            if (!vartype.IsPrimitive && vartype != typeof(List<int>))
            {
                Console.WriteLine($"Unsupported type for LocalState: {vartype}");
                Environment.Exit(1);
            }

            typeBuilder.DefineField($"v{var.Index}", vartype, System.Reflection.FieldAttributes.Public);
        }

        Type type = typeBuilder.CreateType();

        Func<object, object, bool> eq = GenerateLocalStateEqMethod(type);
        Func<object, object> clone = GenerateLocalStateCloneMethod(type);

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
            c.Emit(OpCodes.Ldfld, il.Import(type.GetField($"v{local}", (BindingFlags)(-1))));
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
            c.Emit(OpCodes.Stfld, il.Import(type.GetField($"v{local}", (BindingFlags)(-1))));
        }

        return new(type, eq, clone);
    }

    private static Func<object, object, bool> GenerateLocalStateEqMethod(Type type)
    {
        ParameterExpression param1 = Expression.Parameter(typeof(object));
        ParameterExpression param2 = Expression.Parameter(typeof(object));

        ParameterExpression param1Typed = Expression.Variable(type);
        ParameterExpression param2Typed = Expression.Variable(type);

        List<Expression> exprs =
        [
            Expression.Assign(
                param1Typed,
                Expression.TypeAs(param1, type)
            ),
            Expression.Assign(
                param2Typed,
                Expression.TypeAs(param2, type)
            ),
        ];

        LabelTarget returnLabel = Expression.Label(typeof(bool));

        MethodInfo intListEqMethod = typeof(LocalStateInfo).GetMethod(nameof(IntListEq), (BindingFlags)(-1));

        foreach (FieldInfo field in type.GetFields())
        {
            Expression neq;

            if (field.FieldType == typeof(List<int>))
            {
                neq = Expression.Not(
                    Expression.Call(
                        intListEqMethod,
                        Expression.Field(param1Typed, field),
                        Expression.Field(param2Typed, field)
                    )
                );
            }
            else
            {
                neq = Expression.NotEqual(
                    Expression.Field(param1Typed, field),
                    Expression.Field(param2Typed, field)
                );
            }

            exprs.Add(
                Expression.IfThen(
                    neq,
                    Expression.Return(returnLabel, Expression.Constant(false))
                )
            );
        }

        exprs.Add(Expression.Label(returnLabel, Expression.Constant(true)));
        BlockExpression block = Expression.Block([param1Typed, param2Typed], exprs);

        return Expression.Lambda<Func<object, object, bool>>(block, [param1, param2]).Compile();
    }

    private static Func<object, object> GenerateLocalStateCloneMethod(Type type)
    {
        ParameterExpression param = Expression.Parameter(typeof(object));

        ParameterExpression paramTyped = Expression.Variable(type);

        ParameterExpression result = Expression.Variable(type);

        List<Expression> exprs =
        [
            Expression.Assign(
                paramTyped,
                Expression.TypeAs(param, type)
            ),
        ];

        MethodInfo initializer = typeof(FormatterServices).GetMethod("GetSafeUninitializedObject", (BindingFlags)(-1));

        MethodInfo intListCloneMethod = typeof(LocalStateInfo).GetMethod(nameof(IntListClone), (BindingFlags)(-1));

        exprs.Add(
            Expression.Assign(
                result,
                Expression.TypeAs(
                    Expression.Call(
                        initializer,
                        Expression.Constant(type)
                    ),
                type)
            )
        );

        foreach (FieldInfo field in type.GetFields())
        {
            Expression value;

            if (field.FieldType == typeof(List<int>))
            {
                value = Expression.Call(
                    intListCloneMethod,
                    Expression.Field(paramTyped, field)
                );
            }
            else
            {
                value = Expression.Field(
                    paramTyped,
                    field
                );
            }

            exprs.Add(Expression.Assign(
                Expression.Field(
                    result,
                    field
                ),
                value
            ));
        }

        exprs.Add(result);

        BlockExpression block = Expression.Block([paramTyped, result], exprs);

        return Expression.Lambda<Func<object, object>>(block, [param]).Compile();
    }

    static bool IntListEq(List<int> a, List<int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    static List<int> IntListClone(List<int> l)
    {
        List<int> clone = new();
        clone.Capacity = l.Count;

        foreach (int v in l)
        {
            clone.Add(v);
        }

        return clone;
    }
}

