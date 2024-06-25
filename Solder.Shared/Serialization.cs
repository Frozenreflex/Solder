using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Solder.Shared;

public class SerializedPortInfo
{
    [JsonInclude]
    public string Name;
    [JsonInclude]
    public int Count;
}

public class SerializedProtofluxNode
{
    [JsonInclude] public Guid Guid { get; set; }
    [JsonInclude]
    public float X;
    [JsonInclude]
    public float Y;
    [JsonInclude] public TypeSerialization Type { get; set; }
    [JsonInclude] public List<SerializedPortInfo> SerializedPorts { get; set; } = new();
    [JsonInclude] public List<SerializedGlobalRef> GlobalRefs { get; set; } = new();
    [JsonInclude] public List<SerializedExtra> Extras { get; set; } = new();
}

public class SerializedConnection
{
    [JsonInclude] public Guid FromGuid { get; set; }
    [JsonInclude] public string FromName { get; set; }
    [JsonInclude] public int FromIndex { get; set; }
    [JsonInclude] public Guid ToGuid { get; set; }
    [JsonInclude] public string ToName { get; set; }
    [JsonInclude] public int ToIndex { get; set; }
}
public class SerializedConnections
{
    [JsonInclude] public List<SerializedConnection> InputOutputConnections { get; set; } = new();
    [JsonInclude] public List<SerializedConnection> ImpulseOperationConnections { get; set; } = new();
    [JsonInclude] public List<SerializedConnection> ReferenceConnections { get; set; } = new();

    [JsonIgnore]
    public List<SerializedConnection> AllConnections => InputOutputConnections.Concat(ImpulseOperationConnections)
        .Concat(ReferenceConnections).ToList();
}

public class SerializedGlobalRef
{
    [JsonInclude] public string Name { get; set; }
    [JsonInclude] public bool Drive { get; set; }
    [JsonInclude] public string Value { get; set; }
    [JsonInclude] public int Index { get; set; } = -1; //this should always be -1 until globalreflists are added, this is here for compatibility
}
public class SerializedExtra
{
    [JsonInclude] public string Name { get; set; }
    [JsonInclude] public string Value { get; set; }
}

public class SerializedComment
{
    [JsonInclude] public string Message { get; set; }
    [JsonInclude] public float XPosition { get; set; }
    [JsonInclude] public float YPosition { get; set; }
}

public class SerializedImportName
{
    [JsonInclude] public TypeSerialization Type { get; set; }
    [JsonInclude] public List<string> Names { get; set; }
}
public class SerializedMetadata
{
    [JsonInclude] public float ColorR = 1;
    [JsonInclude] public float ColorG = 1;
    [JsonInclude] public float ColorB = 1;
}
public class SerializedScript
{
    [JsonInclude] public int Version = 1;
    [JsonInclude] public bool RearrangeExport = false;
    [JsonInclude] public List<SerializedProtofluxNode> Nodes { get; set; } = new();
    [JsonInclude] public SerializedConnections Connections { get; set; } = new();
    [JsonInclude] public List<SerializedComment> Comments { get; set; } = new();
    [JsonInclude] public List<SerializedImportName> ImportNames { get; set; } = new();
    [JsonInclude] public SerializedMetadata Metadata { get; set; } = new();
}

public class TypeSerialization
{
    [JsonInclude]
    public string FullTypeName;
    [JsonInclude]
    public List<TypeSerialization> GenericParameters = new();
    public TypeSerialization()
    {
        
    }
    public TypeSerialization(Type type)
    {
        if (type is null) return;
        if (type == typeof(void)) return;
        FullTypeName = type.IsGenericType ? type.GetGenericTypeDefinition().ToString() : type.ToString();
        if (type.IsGenericType)
            GenericParameters.AddRange(type.GetGenericArguments().Select(i => new TypeSerialization(i)));
    }
    public Type GetType(IEnumerable<Type> allowedTypes)
    {
        if (FullTypeName is null) return null;
        var numGeneric = GenericParameters.Count;
        if (numGeneric == 0)
            return allowedTypes.FirstOrDefault(i =>
                i.ToString() == FullTypeName && !i.IsGenericType);
        var parameters = GenericParameters.Select(i => i.GetType(allowedTypes)).ToArray();
        if (parameters.Any(i => i is null)) return null;
        var type = allowedTypes.FirstOrDefault(i =>
            i.ToString() == FullTypeName && i.IsGenericType && i.GetGenericArguments().Length == numGeneric);
        if (type is null) return null;
        try
        {
            return type.MakeGenericType(parameters);
        }
        catch
        {
            return null;
        }
    }
}