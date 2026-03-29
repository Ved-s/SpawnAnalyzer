using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Terraria;

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
        {typeof(List<int>), Utils.GetMethodOrThrow<StateType>(nameof(IntListEq))},
        {typeof(int[]),     Utils.GetMethodOrThrow<StateType>(nameof(IntArray1DEq))},
        {typeof(int[,]),    Utils.GetMethodOrThrow<StateType>(nameof(IntArray2DEq))},
        {typeof(Tile),      Utils.GetMethodOrThrow<StateType>(nameof(TileEq))},
    };

    static readonly Dictionary<Type, MethodInfo> CloneMethods = new()
    {
        {typeof(List<int>), Utils.GetMethodOrThrow<StateType>(nameof(IntListClone))},
        {typeof(int[]),     Utils.GetMethodOrThrow<StateType>(nameof(IntArray1DClone))},
        {typeof(int[,]),    Utils.GetMethodOrThrow<StateType>(nameof(IntArray2DClone))},
        {typeof(Tile),      Utils.GetMethodOrThrow<StateType>(nameof(TileClone))},
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

    public static Func<object, object, bool> GenerateEqMethod(Type type)
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
            if (field.IsStatic)
                continue;
                
            Expression neq;

            if (EqMethods.TryGetValue(field.FieldType, out MethodInfo? eqMethod))
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

    public static Func<object, object> GenerateCloneMethod(Type type)
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

#pragma warning disable SYSLIB0050 // Type or member is obsolete
        MethodInfo initializer = Utils.GetMethodOrThrow(typeof(FormatterServices), "GetSafeUninitializedObject");
#pragma warning restore SYSLIB0050 // Type or member is obsolete

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
            if (field.IsStatic)
                continue;

            Expression value;

            if (CloneMethods.TryGetValue(field.FieldType, out MethodInfo? cloneMethod))
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

    static bool IntListEq(List<int>? a, List<int>? b)
    {
        if (a is null != b is null)
        {
            return false;
        }
        if (a is null || b is null)
        {
            return true;
        }

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

    static List<int>? IntListClone(List<int>? l)
    {
        if (l is null)
        {
            return null!;
        }
        return new(l);
    }

    static bool IntArray1DEq(int[]? a, int[]? b)
    {
        if (a is null != b is null)
        {
            return false;
        }
        if (a is null || b is null)
        {
            return true;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    static int[]? IntArray1DClone(int[]? a)
    {
        if (a is null)
        {
            return null;
        }

        int[] clone = new int[a.Length];

        for (int i = 0; i < a.Length; i++)
        {
            clone[i] = a[i];
        }

        return clone;
    }

    static bool IntArray2DEq(int[,]? a, int[,]? b)
    {
        if (a is null != b is null)
        {
            return false;
        }
        if (a is null || b is null)
        {
            return true;
        }

        int l0 = a.GetLength(0);
        int l1 = a.GetLength(1);
        if (l0 != b.GetLength(0) || l1 != b.GetLength(1))
        {
            return false;
        }

        for (int i = 0; i < l0; i++)
            for (int j = 0; j < l1; j++)
            {
                if (a[i, j] != b[i, j])
                {
                    return false;
                }
            }

        return true;
    }

    static int[,]? IntArray2DClone(int[,]? a)
    {
        if (a is null)
        {
            return null;
        }
        
        int l0 = a.GetLength(0);
        int l1 = a.GetLength(1);

        int[,] clone = new int[l1, l1];

        for (int i = 0; i < l0; i++)
            for (int j = 0; j < l1; j++)
            {
                clone[i, j] = a[i, j];
            }

        return clone;
    }

    static bool TileEq(Tile? a, Tile? b) {
        if (ReferenceEquals(a, b))
            return true;
        
        if (a is null || b is null)
            return false;

        return a.type == b.type &&
		    a.wall == b.wall &&
		    a.liquid == b.liquid &&
		    a.sTileHeader == b.sTileHeader &&
		    a.bTileHeader == b.bTileHeader &&
		    a.bTileHeader2 == b.bTileHeader2 &&
		    a.bTileHeader3 == b.bTileHeader3 &&
		    a.frameX == b.frameX &&
		    a.frameY == b.frameY;
    }

    static Tile? TileClone(Tile? a) {
        if (a is null)
            return null;

        return new Tile(a);
    }
}