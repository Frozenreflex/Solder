using System;
using System.Linq;
using Elements.Core;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using Godot;
using ProtoFlux.Core;

// ReSharper disable StaticMemberInGenericType

namespace Solder.Editor;

public static class FakeCoder<T>
{
    public static bool SupportsStandardBooleanOperations { get; private set; }
    public static Type ANDNode { get; private set; }
    public static Type NANDNode { get; private set; }
    public static Type NORNode { get; private set; }
    public static Type NOTNode { get; private set; }
    public static Type ORNode { get; private set; }
    public static Type XORNode { get; private set; }
    public static Type XNORNode { get; private set; }
    
    public static bool SupportsBooleanRotation { get; private set; }
    public static Type RotateLeftNode { get; private set; }
    public static Type RotateRightNode { get; private set; }
    public static bool SupportsBooleanShifting { get; private set; }
    public static Type ShiftLeftNode { get; private set; }
    public static Type ShiftRightNode { get; private set; }
    
    
    public static bool SupportsApproximately { get; private set; }
    public static Type ApproximatelyNode { get; private set; }
    
    public static bool SupportsApproximatelyNot { get; private set; }
    public static Type ApproximatelyNotNode { get; private set; }
    
    public static bool SupportsDistance { get; private set; }
    public static Type DistanceNode { get; private set; }
    
    public static bool SupportsComparison { get; private set; }
    public static Type GreaterThanNode { get; private set; }
    public static Type GreaterOrEqualNode { get; private set; }
    public static Type LessThanNode { get; private set; }
    public static Type LessOrEqualNode { get; private set; }
    public static bool SupportsMask { get; private set; }
    public static Type MaskNode { get; private set; }
    

    private static readonly string[] BooleanTypeList = {
        "int",
        "long",
        "uint",
        "ulong"
    };

    static FakeCoder()
    {
        if (typeof(T).IsUnmanaged())
        {
            var types = typeof(AND_Bool).Assembly.GetTypes();
            var name = typeof(T).GetNiceName();
            var upperName = char.ToUpper(name[0]) + name[1..];
            
            GD.Print(name);
            
            if (name.StartsWith("bool"))
            {
                SupportsStandardBooleanOperations = true;
                if (typeof(T) != typeof(bool))
                {
                    SupportsBooleanRotation = true;
                    SupportsBooleanShifting = true;
                }
            }
            foreach (var b in BooleanTypeList)
            {
                if (name.StartsWith(b))
                {
                    SupportsStandardBooleanOperations = true;
                }
                if (name == b)
                {
                    SupportsBooleanRotation = true;
                    SupportsBooleanShifting = true;
                }
            }
            if (SupportsStandardBooleanOperations)
            {
                ANDNode = types.FirstOrDefault(i => i.Name == $"AND_{upperName}");
                NANDNode = types.FirstOrDefault(i => i.Name == $"NAND_{upperName}");
                NORNode = types.FirstOrDefault(i => i.Name == $"NOR_{upperName}");
                NOTNode = types.FirstOrDefault(i => i.Name == $"NOT_{upperName}");
                ORNode = types.FirstOrDefault(i => i.Name == $"OR_{upperName}");
                XORNode = types.FirstOrDefault(i => i.Name == $"XOR_{upperName}");
                XNORNode = types.FirstOrDefault(i => i.Name == $"XNOR_{upperName}");
            }
            if (SupportsBooleanRotation)
            {
                RotateLeftNode = types.FirstOrDefault(i => i.Name == $"RotateLeft_{upperName}");
                RotateRightNode = types.FirstOrDefault(i => i.Name == $"RotateRight_{upperName}");
            }
            if (SupportsBooleanShifting)
            {
                ShiftLeftNode = types.FirstOrDefault(i => i.Name == $"ShiftLeft_{upperName}");
                ShiftRightNode = types.FirstOrDefault(i => i.Name == $"ShiftRight_{upperName}");
            }

            NamedNode("Approximately", out var s, out var t);
            SupportsApproximately = s;
            ApproximatelyNode = t;
            
            NamedNode("ApproximatelyNot", out s, out t);
            SupportsApproximatelyNot = s;
            ApproximatelyNotNode = t;
            
            NamedNode("Distance", out s, out t);
            SupportsDistance = s;
            DistanceNode = t;
            
            NamedNode("Mask", out s, out t);
            SupportsMask = s;
            MaskNode = t;

            if (Coder<T>.SupportsComparison)
            {
                SupportsComparison = true;
                GreaterThanNode = typeof(ValueGreaterThan<>).MakeGenericType(typeof(T));
                GreaterOrEqualNode = typeof(ValueGreaterOrEqual<>).MakeGenericType(typeof(T));
                LessThanNode = typeof(ValueLessThan<>).MakeGenericType(typeof(T));
                LessOrEqualNode = typeof(ValueLessOrEqual<>).MakeGenericType(typeof(T));
            }
            else
            {
                NamedNode("GreaterThan", out s, out t);
                SupportsComparison = s;
                GreaterThanNode = t;
                if (SupportsComparison)
                {
                    NamedNode("GreaterOrEqual", out s, out t);
                    GreaterOrEqualNode = t;
                    NamedNode("LessThan", out s, out t);
                    LessThanNode = t;
                    NamedNode("LessOrEqual", out s, out t);
                    LessOrEqualNode = t;
                }
            }
            
            return;
            
            void NamedNode(string nodeStart, out bool supported, out Type nodeType)
            {
                var findType = types.FirstOrDefault(i => i.Name.StartsWith(nodeStart) && i.Name.EndsWith(upperName));
                if (findType is not null)
                {
                    supported = true;
                    nodeType = findType;
                    return;
                }
                supported = false;
                nodeType = null;
            }
        }
    }
}