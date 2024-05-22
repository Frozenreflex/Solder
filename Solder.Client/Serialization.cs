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
using FrooxEngine.ProtoFlux;
using ResoniteModLoader;

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
    [JsonInclude] public string Name { get; private set; }
    [JsonInclude] public bool Drive { get; private set; }
    [JsonInclude] public string Value { get; private set; }
    [JsonInclude] public int Index { get; private set; } = -1;
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

public class SerializedScript
{
    [JsonInclude] public int Version = 1;
    [JsonInclude] public List<SerializedProtofluxNode> Nodes { get; set; } = new();
    [JsonInclude] public SerializedConnections Connections { get; set; } = new();
    [JsonInclude] public List<SerializedComment> Comments { get; set; } = new();
    [JsonInclude] public List<SerializedImportName> ImportNames { get; set; } = new();
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
    public bool Monopack;
    public bool Persistent = true;
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

    public static void DeserializeScript(Slot rootSlot, SerializedScript script, DeserializeSettings settings)
    {
        //remove any already compiled results, for a clean compile
        var children = rootSlot.Children.ToList().Where(c => c.Tag == "Compiled").ToList();
        foreach (var c in children) c.Destroy();

        Slot monoPackRoot = null;
        if (settings.Monopack)
        {
            monoPackRoot = rootSlot.AddSlot("Monopack");
            monoPackRoot.PersistentSelf = settings.Persistent;
            monoPackRoot.Tag = "Compiled";
        }

        var nodeMap = new Dictionary<Guid, ProtoFluxNode>();

        var positionOffset = float3.Zero;

        if (!settings.Monopack)
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
            if (settings.Monopack) parent = monoPackRoot;
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
                var findPort = allLists.FirstOrDefault(i => i.Name == serialized.Name);
                if (findPort is not null && findPort.Count < serialized.Count)
                    for (var i = findPort.Count; i < serialized.Count; i++)
                        findPort.AddElement();
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
                        .MakeGenericMethod(valueType).Invoke(null, [globalRef, parent, find, rootSlot]);
                }
                catch (Exception e)
                {
                    SolderClient.Msg(e.ToString());
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

                if (baseType == typeof(global::FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<>))
                {
                    var extra = extras.FirstOrDefault(i => i.Name == "Value");
                    if (extra is not null)
                    {
                        HandleValueInputExtrasMethod.MakeGenericMethod(first)
                            .Invoke(null, [component, extra, rootSlot]);
                    }
                }
                else if (baseType == typeof(global::FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueObjectInput<>))
                {
                    var extra = extras.FirstOrDefault(i => i.Name == "Value");
                    if (extra is not null)
                    {
                        HandleValueObjectInputExtrasMethod.MakeGenericMethod(first)
                            .Invoke(null, [component, extra, rootSlot]);
                    }
                }
                else if (baseType == typeof(global::FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<>))
                {
                    var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                    if (extra is not null)
                    {
                        HandleValueFieldDriveMethod.MakeGenericMethod(first)
                            .Invoke(null, [component, extra, rootSlot]);
                    }
                }
                else if (baseType == typeof(global::FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ObjectFieldDrive<>))
                {
                    var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                    if (extra is not null)
                    {
                        HandleObjectFieldDriveMethod.MakeGenericMethod(first)
                            .Invoke(null, [component, extra, rootSlot]);
                    }
                }

                else if (baseType == typeof(global::FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ReferenceDrive<>))
                {
                    var extra = extras.FirstOrDefault(i => i.Name == "Drive");
                    if (extra is not null)
                    {
                        HandleReferenceDriveMethod.MakeGenericMethod(first)
                            .Invoke(null, [component, extra, rootSlot]);
                    }
                }
            }
        }

        foreach (var i in script.Connections.ImpulseOperationConnections)
        {
            if (!GetFromTo(i, out var fromNode, out var toNode)) continue;

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

        foreach (var io in script.Connections.InputOutputConnections)
        {
            if (!GetFromTo(io, out var fromNode, out var toNode)) continue;

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

        foreach (var r in script.Connections.ReferenceConnections)
        {
            if (!GetFromTo(r, out var fromNode, out var toNode)) continue;

            var reference = toNode.NodeReferences.FirstOrDefault(i => i.Name == r.ToName);
            if (reference is not null) toNode.TryConnectReference(reference, fromNode, false);
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

    private static void HandleValueFieldDrive<T>(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<T> input,
        SerializedExtra extra, Slot rootSlot) where T : unmanaged
    {
        var findImport = rootSlot.GetComponent<ReferenceMultiplexer<IField<T>>>();
        if (findImport is null) return;
        var value = findImport.References[int.Parse(extra.Value)];
        input.GetRootProxy().Drive.Target = value;
    }

    private static void HandleObjectFieldDrive<T>(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ObjectFieldDrive<T> input,
        SerializedExtra extra, Slot rootSlot)
    {
        var findImport = rootSlot.GetComponent<ReferenceMultiplexer<IField<T>>>();
        if (findImport is null) return;
        var value = findImport.References[int.Parse(extra.Value)];
        input.GetRootProxy().Drive.Target = value;
    }

    private static void HandleReferenceDrive<T>(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ReferenceDrive<T> input,
        SerializedExtra extra, Slot rootSlot) where T : class, IWorldElement
    {
        var findImport = rootSlot.GetComponent<ReferenceMultiplexer<SyncRef<T>>>();
        if (findImport is null) return;
        var value = findImport.References[int.Parse(extra.Value)];
        input.TrySetRootTarget(value);
    }

    private static void HandleValueInputExtras<T>(FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<T> input,
        SerializedExtra extra, Slot rootSlot) where T : unmanaged
    {
        if (SupportedDedicatedEditors.Contains(typeof(T)))
        {
            if (Coder<T>.TryParse(extra.Value, out var value)) input.Value.Value = value;
        }
        else
        {
            var findImport = rootSlot.GetComponent<ValueMultiplexer<T>>();
            if (findImport is null) return;

            input.Value.Value = findImport.Values[int.Parse(extra.Value)];
        }
    }

    private static void HandleValueObjectInputExtras<T>(
        FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueObjectInput<T> input, SerializedExtra extra, Slot rootSlot)
    {
        if (SupportedDedicatedEditors.Contains(typeof(T)))
        {
            if (Coder<T>.TryParse(extra.Value, out var value)) input.Value.Value = value;
        }
        else
        {
            var findImport = rootSlot.GetComponent<ValueMultiplexer<T>>();
            if (findImport is null) return;

            input.Value.Value = findImport.Values[int.Parse(extra.Value)];
        }
    }

    private static void DoGlobalRefValue<T>(SerializedGlobalRef globalRef, Slot parent, ISyncRef find, Slot rootSlot)
    {
        try
        {
            var target = parent.AttachComponent<GlobalValue<T>>();
            find.Target = target;

            //SolderClient.Msg($"GlobalRef {globalRef.Name}, Drive: {globalRef.Drive}, Value: {globalRef.Value}");

            if (globalRef.Drive)
            {
                //SolderClient.Msg($"Drive");
                var index = int.Parse(globalRef.Value);

                var findImport = rootSlot.GetComponent<ValueMultiplexer<T>>();
                if (findImport is null) return;

                if (index >= findImport.Values.Count) return;
                var copyFrom = findImport.Values.GetField(index);

                var copy = parent.AttachComponent<ValueCopy<T>>();
                copy.WriteBack.Value = true;
                copy.Source.Target = copyFrom;
                copy.Target.Target = target.Value;
            }
            else
            {
                if (SupportedDedicatedEditors.Contains(typeof(T)))
                {
                    //SolderClient.Msg($"Dedicated Editor");
                    if (Coder<T>.TryParse(globalRef.Value, out var value))
                    {
                        //SolderClient.Msg($"Parsed Successfully: {value.ToString()}");
                        target.Value.Value = value;
                    }
                }
                else
                {
                    //SolderClient.Msg($"Import");

                    var findImport = rootSlot.GetComponent<ValueMultiplexer<T>>();
                    if (findImport is null) return;

                    target.Value.Value = findImport.Values[int.Parse(globalRef.Value)];
                }
            }
        }
        catch (Exception e)
        {
            SolderClient.Msg(e.ToString());
        }
    }

    private static void DoGlobalRefReference<T>(SerializedGlobalRef globalRef, Slot parent, ISyncRef find,
        Slot rootSlot) where T : class, IWorldElement
    {
        try
        {
            var target = parent.AttachComponent<GlobalReference<T>>();
            find.Target = target;


            var findImport = rootSlot.GetComponent<ReferenceMultiplexer<T>>();
            if (findImport is null) return;

            //SolderClient.Msg($"GlobalRef {globalRef.Name}, Drive: {globalRef.Drive}, Value: {globalRef.Value}");

            var index = int.Parse(globalRef.Value);
            if (index >= findImport.References.Count) return;
            var copyFrom = findImport.References.GetElement(int.Parse(globalRef.Value));

            if (globalRef.Drive)
            {
                //SolderClient.Msg($"Drive");
                var copy = parent.AttachComponent<ReferenceCopy<T>>();
                copy.WriteBack.Value = true;
                copy.Source.Target = copyFrom;
                copy.Target.Target = target.Reference;
            }
            else
            {
                //SolderClient.Msg($"Import");
                target.Reference.Target = copyFrom.Target;
            }
        }
        catch (Exception e)
        {
            SolderClient.Msg(e.ToString());
        }
    }
}
