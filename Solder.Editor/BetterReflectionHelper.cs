using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Casts;
using ProtoFlux.Core;
using Expression = System.Linq.Expressions.Expression;

namespace Solder.Editor;

public static class BetterReflectionHelper
{
    private static List<Type> _castTypes;
    static BetterReflectionHelper()
    {
        _castTypes = typeof(Cast_byte_To_char).Assembly.GetTypes().Where(i =>
            i.Name.Contains("Cast_") && 
            i.BaseType?.GetGenericTypeDefinition() == typeof(ValueCast<,>)).ToList();
    }
    public static Type CastNode(this Type from, Type to)
    {
        if (from.IsUnmanaged() && to == typeof (object))
            return typeof (ValueToObjectCast<>).MakeGenericType(from);
        if (from.IsNullable() && to == typeof (object))
            return typeof (NullableToObjectCast<>).MakeGenericType(Nullable.GetUnderlyingType(from));
        if (from.IsUnmanaged() && from.IsUnmanaged())
        {
            var target = new[] { from, to };
            var tryFind = _castTypes.FirstOrDefault(i => i.BaseType.GetGenericArguments().SequenceEqual(target));
            if (tryFind is not null) return tryFind;
        }
        if ((from.IsClass || from.IsInterface) && (to.IsClass || to.IsInterface) && from.CanCast(to))
            return typeof(ObjectCast<,>).MakeGenericType(from, to);
        return null;
    }
    public static bool CanCast(this Type a, Type b)
    {
        try
        {
            var castExpression = Expression.Convert(Expression.Default(a), b);
            return true;
        }
        catch
        {
            return false;
        }
    }
}