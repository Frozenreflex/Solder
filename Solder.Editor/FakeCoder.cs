using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;

// ReSharper disable StaticMemberInGenericType

namespace Solder.Editor;

internal static class NodeList
{
    public static readonly Type[] List = typeof(AND_Bool).Assembly.GetTypes();
}

public static class FakeCoder<T>
{
    private static readonly Dictionary<string, Type> _nodes = new();
    public static bool Supports(string nodeName, out Type node)
    {
        if (_nodes.TryGetValue(nodeName, out var result))
        {
            node = result;
            return node != null;
        }
        var name = typeof(T).GetNiceName();
        var upperName = char.ToUpper(name[0]) + name[1..];
        var find = NodeList.List.FirstOrDefault(i => i.Name.StartsWith(nodeName) && i.Name.EndsWith(upperName));
        _nodes.Add(nodeName, find);
        node = find;
        return node != null;
    }
    public static bool SupportsStandardBooleanOperations =>
        Supports("AND", out _) ||
        Supports("NAND", out _) ||
        Supports("NOR", out _) ||
        Supports("NOT", out _) ||
        Supports("OR", out _) ||
        Supports("XOR", out _) ||
        Supports("XNOR", out _);
    public static bool SupportsBooleanRotation => Supports("RotateLeft", out _) || Supports("RotateRight", out _);
    public static bool SupportsBooleanShifting => Supports("ShiftLeft", out _) || Supports("ShiftRight", out _);
}