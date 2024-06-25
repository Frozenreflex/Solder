using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;
using Solder.Editor.Nodes;
using Solder.Shared;

namespace Solder.Editor;
public static class GodotScriptSerializer
{
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
    public static SerializedScript SerializeScript(this EditorRoot editor) =>
        new()
        {
            Nodes = editor.NodeGraph.GetChildren().OfType<ProtofluxNode>().Select(SerializeProtofluxNode).ToList(),
            Connections = SerializeConnections(editor.NodeGraph),
            Comments = editor.NodeGraph.GetChildren().OfType<CommentNode>().Select(i => new SerializedComment()
            {
                Message = i.Text.Text,
                XPosition = i.PositionOffset.X / 1000,
                YPosition = -i.PositionOffset.Y / 1000,
            }).ToList(),
            ImportNames = editor.ImportNameMap.Select(i => new SerializedImportName()
            {
                Names = new List<string>(i.Value),
                Type = new TypeSerialization(i.Key)
            }).ToList(),
        };
    public static void DeserializeScript(this EditorRoot editor, SerializedScript script)
    {
        var graph = editor.NodeGraph;
        graph.ClearConnections();
        
        editor.ImportNameMap.Clear();
        foreach (var name in script.ImportNames)
            editor.ImportNameMap.Add(name.Type.GetType(AllTypes), new List<string>(name.Names));
        foreach (var c in graph.GetChildren().OfType<ProtofluxNode>().ToList())
        {
            graph.RemoveChild(c);
            c.QueueFree();
        }
        foreach (var c in graph.GetChildren().OfType<CommentNode>().ToList())
        {
            graph.RemoveChild(c);
            c.QueueFree();
        }
        foreach (var node in script.Nodes.Select(DeserializeProtofluxNode)) graph.AddChild(node);
        DeserializeConnections(graph, script.Connections);
        foreach (var comment in script.Comments)
        {
            var c = new CommentNode();
            graph.AddChild(c);
            c.Text.Text = comment.Message;
            c.PositionOffset = new Vector2(comment.XPosition * 1000, -comment.YPosition * 1000);
        }

        if (script.RearrangeExport) editor.NodeGraph.ArrangeNodes(); 
        //RearrangeExport is used as a flag, when exporting nodes from resonite they're more than likely not aligned to
        //Z=0, so we have to rearrange those to create a visible output
    }
    public static SerializedScript Copy(this GraphEdit graph)
    {
        return new SerializedScript
        {
            Nodes = graph.GetChildren().OfType<ProtofluxNode>().Where(i => i.Selected).Select(SerializeProtofluxNode).ToList(),
            Connections = SerializeConnections(graph, true),
            Comments = graph.GetChildren().OfType<CommentNode>().Where(i => i.Selected).Select(i => new SerializedComment
            {
                Message = i.Text.Text,
                XPosition = i.PositionOffset.X / 1000,
                YPosition = -i.PositionOffset.Y / 1000,
            }).ToList()
        };;
    }

    public static void Paste(this GraphEdit graph, SerializedScript copy)
    {
        foreach (var node in graph.GetChildren().OfType<GraphNode>()) node.Selected = false;
        
        var guidMap = new Dictionary<Guid, Guid>();
        var nodes = copy.Nodes.Select(DeserializeProtofluxNode).ToList();
        foreach (var n in nodes)
        {
            var oldGuid = n.Guid;
            var newGuid = Guid.NewGuid();
            n.Guid = newGuid;
            guidMap.Add(oldGuid, newGuid);
        }
        foreach (var connection in copy.Connections.AllConnections)
        {
            connection.FromGuid = guidMap[connection.FromGuid];
            connection.ToGuid = guidMap[connection.ToGuid];
        }
        foreach (var node in nodes)
        {
            graph.AddChild(node);
            node.PositionOffset += Vector2.One * graph.SnappingDistance;
            node.Selected = true;
        }
        DeserializeConnections(graph, copy.Connections, false);
        foreach (var comment in copy.Comments)
        {
            var c = new CommentNode();
            graph.AddChild(c);
            c.Text.Text = comment.Message;
            c.PositionOffset = new Vector2(comment.XPosition * 1000, -comment.YPosition * 1000);
            c.PositionOffset += Vector2.One * graph.SnappingDistance;
            c.Selected = true;
        }
    }
    public static SerializedProtofluxNode SerializeProtofluxNode(this ProtofluxNode node)
    {
        var serialization = new SerializedProtofluxNode
        {
            Type = new TypeSerialization(node.Type),
            Guid = node.Guid,
            SerializedPorts = node.LeftPortInfo.Concat(node.RightPortInfo)
                .Where(port => port.Count.HasValue)
                .Select(port => new SerializedPortInfo { Name = port.Name, Count = port.Count.Value }).ToList(),
            X = node.PositionOffset.X / 1000,
            Y = -node.PositionOffset.Y / 1000,
            GlobalRefs = node.GlobalRefInfos.Select(i => new SerializedGlobalRef
            {
                Drive = i.Value.Drive,
                Name = i.Name,
                Value = i.Value.Value,
            }).ToList(),
            Extras = node.Extra.Select(i => new SerializedExtra
            {
                Name = i.Name,
                Value = i.Value,
            }).ToList(),
        };
        return serialization;
    }
    public static ProtofluxNode DeserializeProtofluxNode(SerializedProtofluxNode serialization)
    {
        var node = new ProtofluxNode();
        //TODO
        node.Type = serialization.Type.GetType(AllTypes);
        node.Guid = serialization.Guid;
        node.PositionOffset = new Vector2(serialization.X * 1000, serialization.Y * -1000);
        
        node.Initialize();

        foreach (var port in node.LeftPortInfo.Concat(node.RightPortInfo))
        {
            foreach (var v in serialization.SerializedPorts.Where(v => port.Name == v.Name))
            {
                port.Count = v.Count;
            }
        }
        
        foreach (var globalRef in serialization.GlobalRefs)
        {
            var find = node.GlobalRefInfos.FirstOrDefault(i => i.Name == globalRef.Name);
            if (find is null) continue;
            find.Value.Drive = globalRef.Drive;
            find.Value.Value = globalRef.Value;
        }

        foreach (var extra in serialization.Extras)
        {
            var find = node.Extra.FirstOrDefault(i => i.Name == extra.Name);
            if (find is null) continue;
            find.Value = extra.Value;
        }
        
        return node;
    }
    
    public static SerializedConnections SerializeConnections(GraphEdit graph, bool selectedOnly = false)
    {
        var result = new SerializedConnections();
        var connections = graph.GetConnections();
        foreach (var connection in connections)
        {
            if (connection.From is not ProtofluxNode fromFlux || connection.To is not ProtofluxNode toFlux) continue;

            if (selectedOnly)
            {
                if (!fromFlux.Selected || !toFlux.Selected) continue;
            }
            
            var fromPort = fromFlux.BakedRight[connection.FromPort];
            var toPort = toFlux.BakedLeft[connection.ToPort];

            var connectionSerialized = new SerializedConnection
            {
                FromGuid = fromFlux.Guid,
                FromIndex = fromPort.Index ?? -1,
                FromName = fromPort.ParentPort.Name,
                ToGuid = toFlux.Guid,
                ToIndex = toPort.Index ?? -1,
                ToName = toPort.ParentPort.Name,
            };
            
            if (fromPort.ParentPort.ReferenceType is not null) result.ReferenceConnections.Add(connectionSerialized);
            else if (TypeMap.AllImpulseTypes.Contains(fromPort.ParentPort.Type)) result.ImpulseOperationConnections.Add(connectionSerialized);
            else result.InputOutputConnections.Add(connectionSerialized);
        }
        return result;
    }

    public static void DeserializeConnections(GraphEdit graph, SerializedConnections connections, bool clear = true)
    {
        //no, these are not checked
        //i don't care
        if (clear) graph.ClearConnections();
        var fluxNodes = graph.GetChildren().OfType<ProtofluxNode>().ToList();
        foreach (var c in connections.AllConnections)
        {
            var fromNode = fluxNodes.FirstOrDefault(i => i.Guid == c.FromGuid);
            var toNode = fluxNodes.FirstOrDefault(i => i.Guid == c.ToGuid);
            if (fromNode is null || toNode is null) continue;
            
            var fromPort = fromNode.RightPortInfo.FirstOrDefault(i => i.Name == c.FromName);
            var toPort = toNode.LeftPortInfo.FirstOrDefault(i => i.Name == c.ToName);
            if (fromPort is null || toPort is null) continue;
            
            var fromBaked = fromNode.BakedRight.FirstOrDefault(i =>
                i.ParentPort == fromPort && (!i.Index.HasValue || i.Index == c.FromIndex));
            var toBaked = toNode.BakedLeft.FirstOrDefault(i =>
                i.ParentPort == toPort && (!i.Index.HasValue || i.Index == c.ToIndex));
            
            if (fromBaked is null || toBaked is null) continue;
            graph.ConnectNode(fromNode.Name, fromNode.BakedRight.IndexOf(fromBaked), toNode.Name,
                toNode.BakedLeft.IndexOf(toBaked));
        }
    }
}