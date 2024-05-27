using System.Linq;
using Godot;
using Solder.Editor.Nodes;

namespace Solder.Editor;

public partial class EditorGraph : GraphEdit
{
	public override void _Ready()
	{
		base._Ready();
		ShowArrangeButton = false;
	}

	public override bool _IsNodeHoverValid(StringName fromNode, int fromPort, StringName toNode, int toPort)
	{
		var children = GetChildren();
		
		var from = children.OfType<GraphNode>().FirstOrDefault(i => i.Name == fromNode);
		if (from is not ProtofluxNode fromFlux) return Valid(fromNode, fromPort, toNode, toPort);
		var to = children.OfType<GraphNode>().FirstOrDefault(i => i.Name == toNode);
		if (to is not ProtofluxNode toFlux) return Valid(fromNode, fromPort, toNode, toPort);
		var fromType = fromFlux.GetOutputPortType(fromPort);
		var toType = toFlux.GetInputPortType(toPort);
		
		if (fromType == TypeMap.ReferenceType && toType == TypeMap.ReferenceType)
		{
			var fromRealRefType = fromFlux.BakedRight[fromFlux.GetOutputPortSlot(fromPort)].ParentPort.ReferenceType;
			var toRealRefType = toFlux.BakedLeft[toFlux.GetInputPortSlot(toPort)].ParentPort.ReferenceType;
		
			return fromRealRefType.CanCast(toRealRefType);
		}
		if (TypeMap.NotStandardType.Contains(fromType) || TypeMap.NotStandardType.Contains(toType)) return Valid(fromNode, fromPort, toNode, toPort);
		
		return Valid(fromNode, fromPort, toNode, toPort);
	}

	private bool Valid(StringName fromNode, int fromPort, StringName toNode, int toPort)
	{
		if (fromNode == toNode) return false;
		var children = GetChildren();
		var from = children.OfType<GraphNode>().FirstOrDefault(i => i.Name == fromNode);
		var to = children.OfType<GraphNode>().FirstOrDefault(i => i.Name == toNode);
		if (from is null || to is null) return false;
		var fromType = from.GetOutputPortType(fromPort);
		var toType = to.GetInputPortType(toPort);
		return fromType == toType || IsValidConnectionType(fromType, toType);
	}
}
