using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace SpawnAnalyzer;

public class StateType
{
    static readonly AssemblyBuilder TypeAssembly =
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("StateAutoTypes"),
            AssemblyBuilderAccess.Run
        );

    static readonly ModuleBuilder TypeModule = TypeAssembly.DefineDynamicModule("Types");

    static readonly Dictionary<Type, MethodInfo> EqMethods = new()
    {
        {typeof(List<int>), typeof(StateType).GetMethod(nameof(IntListEq), (BindingFlags)(-1))}
    };

    static readonly Dictionary<Type, MethodInfo> CloneMethods = new()
    {
        {typeof(List<int>), typeof(StateType).GetMethod(nameof(IntListClone), (BindingFlags)(-1))}
    };

    public Type Type;

    public new Func<object, object, bool> Equals;
    public Func<object, object> Clone;

    public StateType(Type type, Func<object, object, bool> equals, Func<object, object> clone)
    {
        Type = type;
        Equals = equals;
        Clone = clone;
    }

    public static string GetFieldName(int index) => $"v{index}";

    public static StateType Generate(string typeName, IEnumerable<Type> fieldTypes)
    {
        TypeBuilder typeBuilder = TypeModule.DefineType(typeName);

        int index = 0;
        foreach (Type fieldType in fieldTypes)
        {
            typeBuilder.DefineField(GetFieldName(index), fieldType, FieldAttributes.Public);
            index++;
        }

        Type type = typeBuilder.CreateType();

        Func<object, object, bool> eq = GenerateEqMethod(type);
        Func<object, object> clone = GenerateCloneMethod(type);

        return new StateType(type, eq, clone);
    }

    private static Func<object, object, bool> GenerateEqMethod(Type type)
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

        foreach (FieldInfo field in type.GetFields())
        {
            Expression neq;

            if (EqMethods.TryGetValue(field.FieldType, out MethodInfo eqMethod))
            {
                neq = Expression.Not(
                    Expression.Call(
                        eqMethod,
                        Expression.Field(param1Typed, field),
                        Expression.Field(param2Typed, field)
                    )
                );
            }
            else if (field.FieldType.IsValueType)
            {
                neq = Expression.NotEqual(
                    Expression.Field(param1Typed, field),
                    Expression.Field(param2Typed, field)
                );
            }
            else
            {
                throw new NotImplementedException($"StateType Equals isn't implemented for {field.FieldType.FullName}");
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

    private static Func<object, object> GenerateCloneMethod(Type type)
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

            if (CloneMethods.TryGetValue(field.FieldType, out MethodInfo cloneMethod))
            {
                value = Expression.Call(
                    cloneMethod,
                    Expression.Field(paramTyped, field)
                );
            }
            else if (field.FieldType.IsValueType)
            {
                value = Expression.Field(
                    paramTyped,
                    field
                );
            }
            else
            {
                throw new NotImplementedException($"StateType Equals isn't implemented for {field.FieldType.FullName}");
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