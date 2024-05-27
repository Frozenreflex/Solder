using System;
using System.Linq;
using System.Reflection;
using FrooxEngine;

namespace Solder.Client;

public static class DynamicVariableHelper
{
    public static string SanitizeName(string str) =>
        new(str.Where(i => 
            i is not '/' && 
            (i is ' ' or '.' or '_' || !(char.IsSymbol(i) || char.IsPunctuation(i) || char.IsWhiteSpace(i)))
        ).ToArray());
    private static readonly MethodInfo EnsureDynamicValueVariableMethod =
        typeof(DynamicVariableHelper).GetMethod(nameof(InternalEnsureDynamicValueVariable), BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo EnsureDynamicReferenceVariableMethod =
        typeof(DynamicVariableHelper).GetMethod(nameof(InternalEnsureDynamicReferenceVariable), BindingFlags.NonPublic | BindingFlags.Static);
    public static void EnsureDynamicValueVariable(Slot slot, string name, string setName, Type type) => 
        EnsureDynamicValueVariableMethod.MakeGenericMethod(type).Invoke(null, [slot, name, setName]);
    public static void EnsureDynamicReferenceVariable(Slot slot, string name, string setName, Type type) =>
        EnsureDynamicReferenceVariableMethod.MakeGenericMethod(type).Invoke(null, [slot, name, setName]);
    private static void InternalEnsureDynamicValueVariable<T>(Slot slot, string name, string setName) =>
        slot.GetComponentOrAttach<DynamicValueVariable<T>>(i => i.VariableName.Value.EndsWith(name)).VariableName.Value = setName;
    
    private static void InternalEnsureDynamicReferenceVariable<T>(Slot slot, string name, string setName) where T : class, IWorldElement =>
        slot.GetComponentOrAttach<DynamicReferenceVariable<T>>(i => i.VariableName.Value.EndsWith(name)).VariableName.Value = setName;
}
