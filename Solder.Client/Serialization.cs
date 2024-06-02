using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using ResoniteModLoader;
using Solder.Client.CompileModes;

namespace Solder.Client;

public class SerializedPortInfo
{
    [JsonInclude] public string Name;
    [JsonInclude] public int Count;
}

public class SerializedProtofluxNode
{
    [JsonInclude] public Guid Guid { get; set; }
    [JsonInclude] public float X { get; set; }
    [JsonInclude] public float Y { get; set; }
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
    [JsonInclude] public int Index { get; set; } = -1;
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
    [JsonInclude] public string FullTypeName;
    [JsonInclude] public List<TypeSerialization> GenericParameters = new();

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

public class DeserializeSettings
{
    public BaseCompileMode Mode;
    public bool Monopack = false;
    public bool Persistent = true;
    public Slot ImportRoot;
    public Dictionary<Type, List<string>> ImportNames;
    public T Import<T>(int index) => Mode.Import<T>(index);
    public Sync<T> ImportValue<T>(int index) => Mode.ImportValue<T>(index);
    public SyncRef<T> ImportReference<T>(int index) where T : class, IWorldElement => Mode.ImportReference<T>(index);
}

public static class ResoniteScriptDeserializer
{
    public static readonly Type[] SupportedDedicatedEditors =
    {
        typeof(bool),
        typeof(bool2),
        typeof(bool3),
        typeof(bool4),
        typeof(byte),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
        typeof(sbyte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(uint2),
        typeof(ulong2),
        typeof(int2),
        typeof(long2),
        typeof(float2),
        typeof(double2),
        typeof(uint3),
        typeof(ulong3),
        typeof(int3),
        typeof(long3),
        typeof(float3),
        typeof(double3),
        typeof(uint4),
        typeof(ulong4),
        typeof(int4),
        typeof(long4),
        typeof(float4),
        typeof(double4),
        typeof(float2x2),
        typeof(double2x2),
        typeof(float3x3),
        typeof(double3x3),
        typeof(float4x4),
        typeof(double4x4),
        typeof(floatQ),
        typeof(doubleQ),
        typeof(char),
        typeof(string),
        typeof(Uri),
        //typeof (DateTime),
        //typeof (TimeSpan),
        typeof(colorX),
        typeof(color),
    };

    public static List<Type> AllTypes
    {
        get
        {
            var l = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    l.AddRange(assembly.GetTypes());
                }
                catch
                {
                    // ignored
                }
            }

            return l;
        }
    }

    private static readonly MethodInfo DoGlobalRefValueMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(DoGlobalRefValue),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo DoGlobalRefReferenceMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(DoGlobalRefReference),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo HandleValueInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleValueInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo HandleAssetInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleAssetInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo HandleValueObjectInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleValueObjectInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo HandleValueFieldDriveMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleValueFieldDrive),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo HandleObjectFieldDriveMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleObjectFieldDrive),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo HandleReferenceDriveMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(HandleReferenceDrive),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static int GetGlobalRefIndex(Dictionary<Type, List<object>> dict, Type type, object element)
    {
        if (!dict.TryGetValue(type, out var refList))
        {
            refList = [];
            dict.Add(type, refList);
        }
        if (!refList.Contains(element)) refList.Add(element);
        return refList.IndexOf(element);
    }
    public static SerializedScript ExportScript(Slot rootSlot)
    {
        var script = new SerializedScript();
        var nodes = rootSlot.Children.SelectMany(i => i.Components.OfType<ProtoFluxNode>()).ToList();
        var aligned = nodes.All(i => MathX.Approximately(i.Slot.LocalPosition.Z, 0));
        if (!aligned) script.RearrangeExport = true; //we aren't aligned, rearrange it on import
        var serializedNodes = nodes.ToDictionary(i => i, i => new SerializedProtofluxNode
        {
            Guid = Guid.NewGuid(),
            X = aligned ? i.Slot.LocalPosition.X : 0,
            Y = aligned ? i.Slot.LocalPosition.Y : 0,
            Type = new TypeSerialization(i.GetType()),
            SerializedPorts = i.NodeImpulseLists.Concat(i.NodeOutputLists).Concat(i.NodeInputLists).Concat(i.NodeOperationLists).Select(j => new SerializedPortInfo()
            {
                Count = j.Count,
                Name = j.Name
            }).ToList()
        });
        script.Nodes = serializedNodes.Select(i => i.Value).ToList();
        
        var globalRefs = new Dictionary<Type, List<object>>();
        
        var serializedConnections = script.Connections;
        foreach (var a in serializedNodes)
        {
            var node = a.Key;
            var serialized = a.Value;
            var guid = serialized.Guid;

            var nodeType = node.GetType();

            if (nodeType.IsGenericType)
            {
                try
                {
                    var baseType = nodeType.GetGenericTypeDefinition();
                    var args = nodeType.GetGenericArguments();
                    if (baseType == typeof(ValueInput<>)) SerializeValueInputExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node]);
                    if (baseType == typeof(AssetInput<>)) SerializeAssetInputExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node, globalRefs]);
                    if (baseType == typeof(ValueObjectInput<>)) SerializeValueObjectInputExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node]);
                    if (baseType == typeof(ValueFieldDrive<>)) SerializeValueFieldDriveExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node, globalRefs]);
                    if (baseType == typeof(ObjectFieldDrive<>)) SerializeObjectFieldDriveExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node, globalRefs]);
                    if (baseType == typeof(ReferenceDrive<>)) SerializeReferenceDriveExtrasMethod.MakeGenericMethod(args.First()).Invoke(null, [serialized, node, globalRefs]);
                }
                catch
                {
                    // ignored
                }
            }
            
            foreach (var globalRef in node.NodeGlobalRefs)
            {
                var target = globalRef.Target;
                if (target is not IGlobalValueProxy globalValueProxy) continue;
                
                var type = globalValueProxy.ValueType;
                if (SupportedDedicatedEditors.Contains(type) || type.IsEnum)
                {
                    serialized.GlobalRefs.Add(new SerializedGlobalRef
                    {
                        Name = globalRef.Name,
                        Value = globalValueProxy.BoxedValue.ToString(),
                    });
                }
                else
                {
                    serialized.GlobalRefs.Add(new SerializedGlobalRef
                    {
                        Name = globalRef.Name,
                        Value = GetGlobalRefIndex(globalRefs, type, globalValueProxy.BoxedValue).ToString(),
                    });
                }
            }

            foreach (var reference in node.NodeReferences)
            {
                var referenceTarget = reference.Target;
                if (referenceTarget is null) continue;
                var targetOwner = nodes.FirstOrDefault(i => i == referenceTarget);
                if (targetOwner is null) continue;
                
                serializedConnections.ReferenceConnections.Add(new SerializedConnection
                {
                    FromGuid = serializedNodes[targetOwner].Guid,
                    FromIndex = -1,
                    FromName = "",
                    ToGuid = guid,
                    ToIndex = -1,
                    ToName = reference.Name,
                });
            }
            
            foreach (var impulse in node.NodeImpulses)
            {
                HandleImpulseOperation(impulse, new SerializedConnection
                {
                    FromGuid = guid,
                    FromIndex = -1,
                    FromName = impulse.Name,
                });
            }
            foreach (var impulseList in node.NodeImpulseLists)
            {
                for (var i = 0; i < impulseList.Count; i++)
                {
                    var element = impulseList.GetElement(i);
                    if (element is not ISyncRef impulse) continue;
                    HandleImpulseOperation(impulse, new SerializedConnection
                    {
                        FromGuid = guid,
                        FromIndex = i,
                        FromName = impulseList.Name,
                    });
                }
            }

            foreach (var input in node.NodeInputs)
            {
                HandleInputOutput(input, new SerializedConnection
                {
                    ToGuid = guid,
                    ToIndex = -1,
                    ToName = input.Name,
                });
            }

            foreach (var inputList in node.NodeInputLists)
            {
                for (var i = 0; i < inputList.Count; i++)
                {
                    var element = inputList.GetElement(i);
                    if (element is not ISyncRef input) continue;
                    HandleInputOutput(input, new SerializedConnection
                    {
                        ToGuid = guid,
                        ToIndex = i,
                        ToName = inputList.Name,
                    });
                }
            }
        }

        foreach (var globalRef in globalRefs)
        {
            var names = new List<string>();
            
            foreach (var r in globalRef.Value)
            {
                if (r is IWorldElement elem)
                {
                    var current = elem;
                    if (current is not Slot or SyncElement)
                    {
                        while (current is not Slot or SyncElement)
                        {
                            if (current is null) break;
                            current = current.Parent;
                        }
                    }
                    names.Add(current?.Name ?? "null");
                }
                else
                {
                    names.Add(r?.ToString() ?? "null");
                }
            }
            script.ImportNames.Add(new SerializedImportName
            {
                Type = new TypeSerialization(globalRef.Key),
                Names = names
            });
        }

        return script;
        
        void HandleImpulseOperation(ISyncRef impulse, SerializedConnection serial)
        {
            var impulseTarget = impulse.Target;
            if (impulseTarget is null) return;
            var targetOwner = nodes.FirstOrDefault(i =>
                i.NodeOperations.Concat(i.NodeOperationLists.SelectMany(j => j.Elements.OfType<INodeOperation>()))
                    .Any(j => j == impulseTarget));
            if (targetOwner is null) return;
            
            serial.ToGuid = serializedNodes[targetOwner].Guid;
            
            var operationListOwner = targetOwner.NodeOperationLists.FirstOrDefault(i => i.Elements.OfType<IWorldElement>().Any(j => j == impulseTarget));
            if (operationListOwner is null)
            {
                serial.ToName = impulseTarget == targetOwner ? "*" : impulseTarget.Name;
                serial.ToIndex = -1;
            }
            else
            {
                serial.ToName = operationListOwner.Name;
                serial.ToIndex = operationListOwner.IndexOfElement((ISyncMember)impulseTarget);
            }
            serializedConnections.ImpulseOperationConnections.Add(serial);
        }

        void HandleInputOutput(ISyncRef input, SerializedConnection serial)
        {
            var inputTarget = input.Target;
            if (inputTarget is null) return;
            var targetOwner = nodes.FirstOrDefault(i =>
                i.NodeOutputs.Concat(i.NodeOutputLists.SelectMany(j => j.Elements.OfType<INodeOutput>()))
                    .Any(j => j == inputTarget));
            if (targetOwner is null) return;
            
            serial.FromGuid = serializedNodes[targetOwner].Guid;
            
            var outputListOwner = targetOwner.NodeOutputLists.FirstOrDefault(i => i.Elements.OfType<IWorldElement>().Any(j => j == inputTarget));
            if (outputListOwner is null)
            {
                serial.FromName = inputTarget == targetOwner ? "*" : inputTarget.Name;
                serial.FromIndex = -1;
            }
            else
            {
                serial.FromName = outputListOwner.Name;
                serial.FromIndex = outputListOwner.IndexOfElement((ISyncMember)inputTarget);
            }
            serializedConnections.InputOutputConnections.Add(serial);
        }
    }

    private static readonly MethodInfo SerializeValueInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeValueInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SerializeAssetInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeAssetInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SerializeValueObjectInputExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeValueObjectInputExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SerializeValueFieldDriveExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeValueFieldDriveExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SerializeObjectFieldDriveExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeObjectFieldDriveExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SerializeReferenceDriveExtrasMethod =
        typeof(ResoniteScriptDeserializer).GetMethod(nameof(SerializeReferenceDriveExtras),
            BindingFlags.Static | BindingFlags.NonPublic);
    private static void SerializeValueInputExtras<T>(SerializedProtofluxNode serial, ValueInput<T> source) where T : unmanaged => SerializeValueExtra(serial, source.Value.Value.ToString(), typeof(T));
    private static void SerializeAssetInputExtras<T>(SerializedProtofluxNode serial, AssetInput<T> source,
        Dictionary<Type, List<object>> refs) where T : class, IAsset => SerializeValueExtra(serial,
        GetGlobalRefIndex(refs, typeof(IAssetProvider<T>), source.Target.Target).ToString(), typeof(int));
    private static void SerializeValueObjectInputExtras<T>(SerializedProtofluxNode serial, ValueObjectInput<T> source) => SerializeValueExtra(serial, source.Value.Value.ToString(), typeof(T));
    private static void SerializeValueExtra(SerializedProtofluxNode serial, string str, Type type)
    {
        if (SupportedDedicatedEditors.Contains(type) || type.IsEnum)
            serial.Extras.Add(new SerializedExtra
            {
                Name = "Value",
                Value = str,
            });
        //TODO: non-dedicated editor types
    }

    private static void SerializeValueFieldDriveExtras<T>(SerializedProtofluxNode serial, ValueFieldDrive<T> drive,
        Dictionary<Type, List<object>> refs) where T : unmanaged =>
        SerializeDriveExtra(serial, typeof(IField<T>), drive.GetRootProxy().Drive.Target, refs);
    private static void SerializeObjectFieldDriveExtras<T>(SerializedProtofluxNode serial, ObjectFieldDrive<T> drive,
        Dictionary<Type, List<object>> refs) =>
        SerializeDriveExtra(serial, typeof(IField<T>), drive.GetRootProxy().Drive.Target, refs);
    private static void SerializeReferenceDriveExtras<T>(SerializedProtofluxNode serial, ReferenceDrive<T> drive,
        Dictionary<Type, List<object>> refs) where T : class, IWorldElement
    {
        //why do the fielddrives have GetRootProxy(), but not referencedrive? thanks froox
        var driveProxy =
            drive.Slot.GetComponent<FrooxEngine.ProtoFlux.CoreNodes.ReferenceDrive<T>.Proxy>(
                p => p.Node.Target == drive);
        SerializeDriveExtra(serial, typeof(SyncRef<T>), driveProxy.Drive.Target, refs);
    }

    private static void SerializeDriveExtra(SerializedProtofluxNode serial, Type targetType, IWorldElement member, Dictionary<Type, List<object>> refs)
    {
        serial.Extras.Add(new SerializedExtra
        {
            Name = "Drive",
            Value = GetGlobalRefIndex(refs, targetType, member).ToString(),
        });
    }

    public static void DeserializeScript(Slot rootSlot, SerializedScript script, DeserializeSettings settings)
    {
        var monopack = settings.Mode.SupportsMonopack && settings.Monopack;
        //remove any already compiled results, for a clean compile
        settings.Mode.CleanupPreviousCompile(rootSlot);
        
        Slot monoPackRoot = null;
        if (monopack)
        {
            monoPackRoot = rootSlot.AddSlot("Monopack");
            monoPackRoot.PersistentSelf = settings.Persistent;
            monoPackRoot.Tag = "Compiled";
        }

        var nodeMap = new Dictionary<Guid, ProtoFluxNode>();

        var positionOffset = float3.Zero;

        if (!monopack)
        {
            var minX = script.Nodes.Min(i => i.X);
            var maxX = script.Nodes.Max(i => i.X);
            var averageX = MathX.Average(minX, maxX);

            var minY = script.Nodes.Min(i => i.Y);
            var maxY = script.Nodes.Max(i => i.Y);
            var averageY = MathX.Average(minY, maxY);
            positionOffset =
                -new float3(averageX, averageY, 0); //offset the nodes so that they're centered to the parent
        }

        foreach (var node in script.Nodes)
        {
            var type = node.Type.GetType(AllTypes);
            if (!type.IsSubclassOf(typeof(ProtoFluxNode))) continue;

            Slot parent;
            if (monopack) parent = monoPackRoot;
            else
            {
                parent = rootSlot.AddSlot(type.Name);
                parent.Tag = "Compiled";
                parent.LocalPosition = new float3(node.X, node.Y, 0) + positionOffset;
                parent.PersistentSelf = settings.Persistent;
            }

            if (parent?.AttachComponent(type) is not ProtoFluxNode component) continue;
            
            nodeMap.Add(node.Guid, component);

            var allLists = component.NodeImpulseLists
                .Concat(component.NodeOperationLists)
                .Concat(component.NodeInputLists)
                .Concat(component.NodeOutputLists).ToList();

            foreach (var serialized in node.SerializedPorts)
            {
                try
                {
                    var findPort = allLists.FirstOrDefault(i => i.Name == serialized.Name);
                    if (findPort is not null && findPort.Count < serialized.Count)
                        for (var i = findPort.Count; i < serialized.Count; i++)
                            findPort.AddElement();
                }
                catch
                {
                    // ignored
                }
            }

            foreach (var globalRef in node.GlobalRefs)
            {
                var find = component.NodeGlobalRefs.FirstOrDefault(i => i.Name == globalRef.Name);
                if (find is null) continue;
                try
                {
                    //SyncRef<IGlobalValueProxy<T>>
                    var valueType = find.GetType().GetGenericArguments().First().GetGenericArguments().First();
                    (valueType.IsEnginePrimitive() ? DoGlobalRefValueMethod : DoGlobalRefReferenceMethod)
                        .MakeGenericMethod(valueType).Invoke(null, [globalRef, parent, find, settings]);
                }
                catch (Exception e)
                {
                    ResoniteMod.Msg(e.ToString());
                }
            }

            var extras = node.Extras;

            if (extras.Count == 0) continue;

            var args = type.GetGenericArguments();

            if (args.Length > 0)
            {
                var baseType = type.GetGenericTypeDefinition();
                var parameters = type.GetGenericArguments();
                var first = parameters.First();

                try
                {
                    if (baseType == typeof(ValueInput<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Value");
                        if (extra is not null)
                        {
                            HandleValueInputExtrasMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                    if (baseType == typeof(AssetInput<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Value");
                        if (extra is not null)
                        {
                            HandleAssetInputExtrasMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                    else if (baseType == typeof(ValueObjectInput<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Value");
                        if (extra is not null)
                        {
                            HandleValueObjectInputExtrasMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                    else if (baseType == typeof(ValueFieldDrive<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                        if (extra is not null)
                        {
                            HandleValueFieldDriveMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                    else if (baseType == typeof(ObjectFieldDrive<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                        if (extra is not null)
                        {
                            HandleObjectFieldDriveMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                    else if (baseType == typeof(ReferenceDrive<>))
                    {
                        var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                        if (extra is not null)
                        {
                            HandleReferenceDriveMethod.MakeGenericMethod(first)
                                .Invoke(null, [component, extra, settings]);
                        }
                    }
                }
                catch
                {
                }
            }
        }

        foreach (var i in script.Connections.ImpulseOperationConnections)
        {
            if (!GetFromTo(i, out var fromNode, out var toNode)) continue;

            try
            {
                var impulse = i.FromIndex != -1
                    ? fromNode.NodeImpulseLists.FirstOrDefault(j => j.Name == i.FromName)?.Elements.OfType<ISyncRef>()
                        .ToList()[i.FromIndex]
                    : fromNode.NodeImpulses.FirstOrDefault(j => j.Name == i.FromName);
                var operation = i.ToIndex != -1
                    ? toNode.NodeOperationLists.FirstOrDefault(j => j.Name == i.ToName)?.Elements.OfType<INodeOperation>()
                        .ToList()[i.ToIndex]
                    : toNode.NodeOperations.FirstOrDefault(j => j.Name == i.ToName);
                if (operation is null && toNode is INodeOperation nodeOperation) operation = nodeOperation;

                if (impulse is not null && operation is not null) toNode.TryConnectImpulse(impulse, operation, false);
            }
            catch
            {
                // ignored
            }
        }

        foreach (var io in script.Connections.InputOutputConnections)
        {
            if (!GetFromTo(io, out var fromNode, out var toNode)) continue;

            try
            {
                var output = io.FromIndex != -1
                    ? fromNode.NodeOutputLists.FirstOrDefault(j => j.Name == io.FromName)?.Elements.OfType<INodeOutput>()
                        .ToList()[io.FromIndex]
                    : fromNode.NodeOutputs.FirstOrDefault(j => j.Name == io.FromName);
                if (output is null && fromNode is INodeOutput nodeOutput) output = nodeOutput;
                var input = io.ToIndex != -1
                    ? toNode.NodeInputLists.FirstOrDefault(j => j.Name == io.ToName)?.Elements.OfType<ISyncRef>()
                        .ToList()[io.ToIndex]
                    : toNode.NodeInputs.FirstOrDefault(j => j.Name == io.ToName);
                if (output is not null && input is not null) toNode.TryConnectInput(input, output, false, false);
            }
            catch
            {
                // ignored
            }
        }

        foreach (var r in script.Connections.ReferenceConnections)
        {
            if (!GetFromTo(r, out var fromNode, out var toNode)) continue;

            try
            {
                var reference = toNode.NodeReferences.FirstOrDefault(i => i.Name == r.ToName);
                if (reference is not null) toNode.TryConnectReference(reference, fromNode, false);
            }
            catch
            {
                // ignored
            }
        }

        return;

        bool GetFromTo(SerializedConnection i, out ProtoFluxNode from, out ProtoFluxNode to)
        {
            from = null;
            to = null;
            if (!nodeMap.TryGetValue(i.FromGuid, out var fromNode)) return false;
            if (!nodeMap.TryGetValue(i.ToGuid, out var toNode)) return false;
            from = fromNode;
            to = toNode;
            return true;
        }
    }

    private static void HandleValueFieldDrive<T>(ValueFieldDrive<T> input,
        SerializedExtra extra, DeserializeSettings settings) where T : unmanaged
    {
        var import = settings.Import<IField<T>>(int.Parse(extra.Value));
        if (import is not null) input.GetRootProxy().Drive.Target = import;
    }

    private static void HandleObjectFieldDrive<T>(ObjectFieldDrive<T> input,
        SerializedExtra extra, DeserializeSettings settings)
    {
        var import = settings.Import<IField<T>>(int.Parse(extra.Value));
        if (import is not null) input.GetRootProxy().Drive.Target = import;
    }

    private static void HandleReferenceDrive<T>(ReferenceDrive<T> input,
        SerializedExtra extra, DeserializeSettings settings) where T : class, IWorldElement
    {
        var import = settings.Import<SyncRef<T>>(int.Parse(extra.Value));
        if (import is not null) input.TrySetRootTarget(import);
    }

    private static void HandleAssetInputExtras<T>(AssetInput<T> input,
        SerializedExtra extra, DeserializeSettings settings) where T : class, IAsset
    {
        if (int.TryParse(extra.Value, out var val)) input.Target.Target = settings.Import<IAssetProvider<T>>(val);
    }
    private static void HandleValueInputExtras<T>(ValueInput<T> input,
        SerializedExtra extra, DeserializeSettings settings) where T : unmanaged
    {
        if (SupportedDedicatedEditors.Contains(typeof(T)))
        {
            if (Coder<T>.TryParse(extra.Value, out var value)) input.Value.Value = value;
        }
        else if (typeof(T).IsEnum)
        {
            //THANKS FROOX
            input.Value.Value = (T)Enum.Parse(typeof(T), extra.Value);
        }
        else if (int.TryParse(extra.Value, out var val)) input.Value.Value = settings.Import<T>(val);
    }

    private static void HandleValueObjectInputExtras<T>(
        ValueObjectInput<T> input, SerializedExtra extra, DeserializeSettings settings)
    {
        if (SupportedDedicatedEditors.Contains(typeof(T)))
        {
            if (Coder<T>.TryParse(extra.Value, out var value)) input.Value.Value = value;
        }
        else if (typeof(T).IsEnum)
        {
            //THANKS FROOX
            input.Value.Value = (T)Enum.Parse(typeof(T), extra.Value);
        }
        else if (int.TryParse(extra.Value, out var val)) input.Value.Value = settings.Import<T>(val);
    }

    private static void DoGlobalRefValue<T>(SerializedGlobalRef globalRef, Slot parent, ISyncRef find, DeserializeSettings settings)
    {
        try
        {
            var target = parent.AttachComponent<GlobalValue<T>>();
            find.Target = target;

            //SolderClient.Msg($"GlobalRef {globalRef.Name}, Drive: {globalRef.Drive}, Value: {globalRef.Value}");

            if (globalRef.Drive)
            {
                var import = settings.ImportValue<T>(int.Parse(globalRef.Value));
                if (import is null) return;
                
                var copy = parent.AttachComponent<ValueCopy<T>>();
                copy.WriteBack.Value = true;
                copy.Source.Target = import;
                copy.Target.Target = target.Value;
            }
            else
            {
                if (SupportedDedicatedEditors.Contains(typeof(T)))
                {
                    if (Coder<T>.TryParse(globalRef.Value, out var value)) target.Value.Value = value;
                }
                else if (typeof(T).IsEnum)
                {
                    //THANKS FROOX
                    target.Value.Value = (T)Enum.Parse(typeof(T), globalRef.Value);
                }
                else if (int.TryParse(globalRef.Value, out var val)) target.Value.Value = settings.Import<T>(val);
            }
        }
        catch (Exception e)
        {
            ResoniteMod.Msg(e.ToString());
        }
    }

    private static void DoGlobalRefReference<T>(SerializedGlobalRef globalRef, Slot parent, ISyncRef find, DeserializeSettings settings) where T : class, IWorldElement
    {
        try
        {
            var target = parent.AttachComponent<GlobalReference<T>>();
            find.Target = target;
            
            if (int.TryParse(globalRef.Value, out var val))
            {
                if (globalRef.Drive)
                {
                    var import = settings.ImportReference<T>(val);
                    //SolderClient.Msg($"Drive");
                    var copy = parent.AttachComponent<ReferenceCopy<T>>();
                    copy.WriteBack.Value = true;
                    copy.Source.Target = import;
                    copy.Target.Target = target.Reference;
                }
                else
                {

                    var import = settings.Import<T>(val);
                    target.Reference.Target = import;
                }
            }
        }
        catch (Exception e)
        {
            ResoniteMod.Msg(e.ToString());
        }
    }
}
