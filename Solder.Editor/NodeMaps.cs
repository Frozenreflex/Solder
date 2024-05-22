using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Rects;
using Godot;
using ProtoFlux.Core;

namespace Solder.Editor;
public static class TypeMap
{
    public delegate void TypeMapUpdatedDelegate(Type type);
    //technically you could break this if you somehow got 2 billion types, but by that point you have bigger problems
    public const int OperationType = int.MaxValue;
    public const int AsyncOperationType = int.MaxValue - 1;
    public const int ContinuationImpulseType = int.MaxValue - 2;
    public const int CallImpulseType = int.MaxValue - 3;
    public const int AsyncCallImpulseType = int.MaxValue - 4;
    public const int SyncResumptionImpulseType = int.MaxValue - 5;
    public const int AsyncResumptionImpulseType = int.MaxValue - 6;
    public const int ReferenceType = int.MaxValue - 7;
    public static readonly int[] SynchronousImpulseTypes =
    {
        ContinuationImpulseType,
        CallImpulseType,
        SyncResumptionImpulseType,
    };
    public static readonly int[] AsynchronousImpulseTypes =
    {
        AsyncCallImpulseType,
        AsyncResumptionImpulseType,
    };
    public static readonly int[] AllImpulseTypes = SynchronousImpulseTypes.Concat(AsynchronousImpulseTypes).ToArray();
    public static readonly int[] AllOperationTypes = { OperationType, AsyncOperationType };
    public static readonly int[] AllImpulseRelatedTypes = AllImpulseTypes.Concat(AllOperationTypes).ToArray();

    public static readonly int[] AllSyncImpulseRelatedTypes =
        SynchronousImpulseTypes.Concat(new[] { OperationType }).ToArray();
    public static readonly int[] AllAsyncImpulseRelatedTypes =
        AsynchronousImpulseTypes.Concat(new[] { AsyncOperationType }).ToArray();
    
    public static readonly int[] NotStandardType = AllImpulseRelatedTypes.Concat(new []{ ReferenceType }).ToArray();

    public static event TypeMapUpdatedDelegate TypeMapUpdated = _ => { };

    private static readonly List<Type> InputOutputTypeMap = new();

    public static IReadOnlyCollection<Type> CurrentTypeMap => InputOutputTypeMap.AsReadOnly();

    public static int GetImpulseTypeIndex(ImpulseType type)
    {
        return type switch
        {
            ImpulseType.Continuation => ContinuationImpulseType,
            ImpulseType.Call => CallImpulseType,
            ImpulseType.AsyncCall => AsyncCallImpulseType,
            ImpulseType.SyncResumption => SyncResumptionImpulseType,
            ImpulseType.AsyncResumption => AsyncResumptionImpulseType,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public static int GetOperationTypeIndex(bool isAsync) => isAsync ? AsyncOperationType : OperationType;

    public static int GetTypeIndex(Type type)
    {
        var index = InputOutputTypeMap.IndexOf(type);
        if (index != -1) return index;
        index = InputOutputTypeMap.Count;
        TypeMapUpdated.Invoke(type);
        InputOutputTypeMap.Add(type);
        return index;
    }
    public static Type FromTypeIndex(int index) => index >= InputOutputTypeMap.Count ? null : InputOutputTypeMap[index];

    public static string GetTypeName(int index) =>
        index switch
        {
            OperationType => "Operation",
            AsyncOperationType => "AsyncOperation",
            ContinuationImpulseType => "Continuation",
            CallImpulseType => "Call",
            AsyncCallImpulseType => "AsyncCall",
            SyncResumptionImpulseType => "SyncResumption",
            AsyncResumptionImpulseType => "AsyncResumption",
            ReferenceType => "Reference",
            _ => FromTypeIndex(index).GetNiceTypeName()
        };
}
public class NodeMetadataMap
{
    public Type BindingType;
    public Type FluxType;
    public readonly NodeMetadata Metadata;

    public NodeMetadataMap(Type type)
    {
        BindingType = type;
        var metaDataObject = Activator.CreateInstance(type) as ProtoFluxNode;
        var t = metaDataObject!.NodeType;
        FluxType = t;
        Metadata = NodeMetadataHelper.GetMetadata(t);
    }
}

public class NodeCategoryMap
{
    public readonly Type Type;
    public NodeCategoryMap(Type type)
    {
        Type = type;
    }
}

public class CategoryMapNode
{
    public string CurrentNodeName => CurrentPath.Length == 0 ? "Root" : CurrentPath.Last();
    public readonly string[] CurrentPath;
    public readonly Dictionary<string, CategoryMapNode> Subcategories;
    public readonly List<NodeCategoryMap> Nodes;

    public CategoryMapNode(string[] currentPath, Dictionary<Type, CategoryAttribute> data, List<string[]> allPaths)
    {
        CurrentPath = currentPath;
        var thisPath = data.Where(i => i.Value.Paths.Any(j => j.Split('/').SequenceEqual(CurrentPath)));
        Nodes = thisPath.Select(i => new NodeCategoryMap(i.Key)).ToList();
        
        var toDo = CurrentPath.Length > 0 ? allPaths.Where(i => i.Length == CurrentPath.Length + 1 && i.SkipLast(1).SequenceEqual(CurrentPath))
            .ToList() : allPaths.Where(i => i.Length == 1).ToList();
        
        Subcategories = toDo.Count > 0 ? toDo.ToDictionary(t => t.Last(), t => new CategoryMapNode(t, data, allPaths)) : new Dictionary<string, CategoryMapNode>();
    }
}

public static class NodeMaps
{
    private static readonly List<Type> CoreNodeWhitelist = new()
    {
    };
    private static readonly Dictionary<Type, NodeMetadataMap> Maps = new();
    public static readonly CategoryMapNode NodeCategoryTree;
    private static readonly List<Type> ValidTypes = new();
    public static ReadOnlyCollection<Type> CurrentValidTypes => ValidTypes.AsReadOnly();

    static NodeMaps()
    {
        foreach (var assembly in new[]{ typeof(ProtoFluxNode).Assembly, typeof(RectToMinMax).Assembly })
        {
            GD.Print(assembly.ToString());
            foreach (var t in assembly.GetTypes())
            {
                if (!t.IsAssignableTo(typeof(ProtoFluxNode)))
                    continue;
                if (!t.GetCustomAttributes().Any(j => j is CategoryAttribute))
                    continue;
                if (t.GetCustomAttributes().OfType<CategoryAttribute>().First().Paths
                    .Any(i => i.Contains("ProtoFlux/FrooxEngine/ProtoFlux/CoreNodes")) && 
                    (t.Name.Contains("Proxy") && !CoreNodeWhitelist.Contains(t))) //remove proxy node spam
                    continue;
                if (t.IsAbstract)
                    continue;
                ValidTypes.Add(t);
            }
        }
        var dict = ValidTypes.ToDictionary(i => i,
            i => i.GetCustomAttributes().OfType<CategoryAttribute>().First());

        var allValidEndPaths = dict
            .SelectMany(i => i.Value.Paths)
            .Distinct()
            .Select(i => i.Split('/'))
            .Where(i => i.Length > 0)
            .ToList();
        var allValidSubPaths = new List<string[]>();
        foreach (var i in allValidEndPaths)
            for (var j = 0; j <= i.Length; j++)//todo: what the fuck? why is it <=?
            {
                var iteration = i[..j];
                if (allValidSubPaths.All(k => !iteration.SequenceEqual(k))) allValidSubPaths.Add(iteration);
            }
        allValidSubPaths = allValidSubPaths.Where(i => i.Length > 0).ToList();

        /*
        foreach (var p in allValidEndPaths) GD.Print(string.Concat(p.Select(i => i + "/")));

        GD.Print("Creating tree");
        */
            
        NodeCategoryTree = new CategoryMapNode(Array.Empty<string>(), dict, allValidSubPaths);
    }
    public static NodeMetadataMap GetMetadataMap(Type type)
    {
        if (Maps.TryGetValue(type, out var value)) return value;
        value = new NodeMetadataMap(type);
        Maps.Add(type, value);
        return value;
    }
}