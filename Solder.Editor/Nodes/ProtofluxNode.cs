using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using Godot;
using ProtoFlux.Core;
using Type = System.Type;

namespace Solder.Editor.Nodes;

public class PortInfo
{
    public string Name;
    public Color Color;
    public int Type;
    public Type ReferenceType; //only used by references
    public int? Count = null; //if this is null, this is singular, if not null this is a list
    public bool Flow = false; //by default, ports on the right can have unlimited connections, but ports on the left can only have one, flow nodes are inverted
}
public class BakedPortInfo
{
    public PortInfo ParentPort;
    public int? Index = null;
}

public class GlobalRefValue
{
    public bool Drive;
    public string Value;
}
public class GlobalRefInfo
{
    public string Name;
    public Color Color;
    public Type Type;
    public GlobalRefValue Value;
}

public class ExtraData
{
    public string Name;
    public Color Color;
    public Type Type;
    public string Value;
}

//TODO: globalreflist is not used, but could be in the future
public partial class ProtofluxNode : GraphNode
{
    public static readonly StyleBoxEmpty StyleEmpty = new();
    public readonly List<PortInfo> LeftPortInfo = new();
    public readonly List<PortInfo> RightPortInfo = new();
    public readonly List<GlobalRefInfo> GlobalRefInfos = new();
    public readonly List<ExtraData> Extra = new();
    public List<BakedPortInfo> BakedLeft { get; private set; }
    public List<BakedPortInfo> BakedRight { get; private set; }
    public Type Type { get; set; }
    public readonly List<Control> ChildrenItems = new();

    public Guid Guid = Guid.NewGuid();
    private bool _initialized;
    
    public static ProtofluxNode CreateNode(Type type)
    {
        var node = new ProtofluxNode();
        node.Type = type;
        node.Initialize();
        return node;
    }
    
    public static ProtofluxNode CreateCastNode(Type a, Type b)
    {
        var node = new ProtofluxNode();
        node.Type = a.CastNode(b);
        node.Initialize();
        return node;
    }
    public void Initialize()
    {
        if (_initialized) return;
        var type = Type;
        var meta = NodeMaps.GetMetadataMap(type);
        var metadata = meta.Metadata;
        
        var name = meta.Metadata.Name;
        if (string.IsNullOrWhiteSpace(name)) name = type.Name;
        Title = name;
        
        //left
        foreach (var operation in metadata.FixedOperations)
        {
            var c = DatatypeColorHelper.GetOperationColor(operation.IsAsync).rgb;
            LeftPortInfo.Add(new PortInfo
            {
                Name = operation.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetOperationTypeIndex(operation.IsAsync),
                Flow = true,
            });
        }
        foreach (var operationList in metadata.DynamicOperations)
        {
            var c = DatatypeColorHelper.GetOperationColor(operationList.SupportsAsync).rgb;
            LeftPortInfo.Add(new PortInfo
            {
                Name = operationList.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetOperationTypeIndex(operationList.SupportsAsync),
                Count = 1,
                Flow = true,
            });
        }
        foreach (var input in metadata.FixedInputs)
        {
            var c = input.InputType.GetTypeColor().rgb;
            LeftPortInfo.Add(new PortInfo
            {
                Name = input.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetTypeIndex(input.InputType),
            });
        }
        foreach (var inputList in metadata.DynamicInputs)
        {
            var c = inputList.TypeConstraint.GetTypeColor().rgb;
            LeftPortInfo.Add(new PortInfo
            {
                Name = inputList.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetTypeIndex(inputList.TypeConstraint),
                Count = 1,
            });
        }

        foreach (var reference in metadata.FixedReferences)
        {
            LeftPortInfo.Add(new PortInfo
            {
                Name = reference.Name,
                Color = Colors.Yellow with { A = 1f },
                Type = TypeMap.ReferenceType,
                ReferenceType = reference.ReferenceType,
            });
        }
        
        //right

        foreach (var impulse in metadata.FixedImpulses)
        {
            var c = impulse.Type.GetImpulseColor().rgb;
            RightPortInfo.Add(new PortInfo
            {
                Name = impulse.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetImpulseTypeIndex(impulse.Type),
                Flow = true,
            });
        }
        foreach (var impulseList in metadata.DynamicImpulses)
        {
            //what the fuck?
            if (impulseList.Type == null) continue;
            var c = impulseList.Type.Value.GetImpulseColor().rgb;
            RightPortInfo.Add(new PortInfo
            {
                Name = impulseList.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetImpulseTypeIndex(impulseList.Type.Value),
                Count = 1,
                Flow = true,
            });
        }
        
        foreach (var output in metadata.FixedOutputs)
        {
            var c = output.OutputType.GetTypeColor().rgb;
            RightPortInfo.Add(new PortInfo
            {
                Name = output.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetTypeIndex(output.OutputType),
            });
        }
        foreach (var outputList in metadata.DynamicOutputs)
        {
            var c = outputList.TypeConstraint.GetTypeColor().rgb;
            RightPortInfo.Add(new PortInfo
            {
                Name = outputList.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = TypeMap.GetTypeIndex(outputList.TypeConstraint),
                Count = 1,
            });
        }
        
        //reference output
        RightPortInfo.Add(new PortInfo
        {
            Name = "",
            Color = Colors.OrangeRed with { A = 0.125f },
            Type = TypeMap.ReferenceType,
            ReferenceType = ((ProtoFluxNode)Activator.CreateInstance(Type))?.NodeType,
        });
        
        //globalrefs
        //TODO: globalreflist is unused right now, but might not be later
        foreach (var globalRef in metadata.FixedGlobalRefs)
        {
            var t = globalRef.ValueType;
            var c = t.GetTypeColor().rgb;

            var val = ValueEdit.Default(t);
            
            GlobalRefInfos.Add(new GlobalRefInfo()
            {
                Name = globalRef.Name,
                Color = new Color(c.x,c.y,c.z),
                Type = t,
                Value = new GlobalRefValue
                {
                    Value = val,
                },
            });
        }

        InitializeExtra();

        _initialized = true;
    }
    public void InitializeExtra()
    {
        var genericArguments = Type.GetGenericArguments();
        if (genericArguments.Length > 0)
        {
            var first = genericArguments.First();
            var baseType = Type.GetGenericTypeDefinition();
            
            if (baseType == typeof(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<>) || 
                baseType == typeof(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ObjectFieldDrive<>))
            {
                var inType = typeof(IField<>).MakeGenericType(first);
                
                var c = inType.GetTypeColor().rgb;
                
                Extra.Add(new ExtraData
                {
                    Name = "Drive",
                    Type = inType,
                    Color = new Color(c.x,c.y,c.z),
                    Value = "0",
                });
            }

            if (baseType == typeof(FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ReferenceDrive<>))
            {
                var inType = typeof(SyncRef<>).MakeGenericType(first);
                
                var c = inType.GetTypeColor().rgb;
                
                Extra.Add(new ExtraData
                {
                    Name = "Drive",
                    Type = inType,
                    Color = new Color(c.x,c.y,c.z),
                    Value = "0",
                });
            }

            if (baseType == typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<>) ||
                baseType == typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueObjectInput<>))
            {
                var c = first.GetTypeColor().rgb;
                
                var val = ValueEdit.Default(first);
                
                Extra.Add(new ExtraData
                {
                    Name = "Value",
                    Type = first,
                    Color = new Color(c.x,c.y,c.z),
                    Value = val,
                });
            }

            if (baseType == typeof(AssetInput<>))
            {
                var inType = typeof(IAssetProvider<>).MakeGenericType(first);
                
                var c = inType.GetTypeColor().rgb;
                
                Extra.Add(new ExtraData
                {
                    Name = "Value",
                    Type = inType,
                    Color = new Color(c.x,c.y,c.z),
                    Value = "0",
                });
            }
        }
    }
    public List<BakedPortInfo> Bake(List<PortInfo> portInfo)
    {
        var list = new List<BakedPortInfo>();
        foreach (var i in portInfo)
        {
            var isList = i.Count.HasValue;
            var count = i.Count ?? 1;
            for (var j = 0; j < count; j++)
            {
                list.Add(new BakedPortInfo
                {
                    ParentPort = i,
                    Index = isList ? j : null,
                });
            }
        }
        return list;
    }

    public void Generate(bool nukeLeft = true, bool nukeRight = true)
    {
        ClearAllSlots();
        foreach (var c in ChildrenItems) c.QueueFree();
        ChildrenItems.Clear();

        GenerateDefault(nukeLeft, nukeRight);
        
        //todo: fix this
        CallDeferred(Control.MethodName.ResetSize);
    }
    
    public void GenerateDefault(bool nukeLeft = true, bool nukeRight = true)
    {
        //todo: find a way to smoothly transition connections, for now we nuke them when we generate
        if (GetParent() is GraphEdit edit)
        {
            var connections = edit.GetConnections().Where(i => (nukeRight && i.From == this) || (nukeLeft && i.To == this));
            foreach (var c in connections) edit.DisconnectNode(c);
        }
        
        BakedLeft = Bake(LeftPortInfo);
        BakedRight = Bake(RightPortInfo);
        
        for (var i = 0; i < BakedLeft.Count; i++)
        {
            var left = BakedLeft[i];
            SetSlotEnabledLeft(i, true);
            SetSlotColorLeft(i, left.ParentPort.Color);
            SetSlotTypeLeft(i, left.ParentPort.Type);
        }
        for (var i = 0; i < BakedRight.Count; i++)
        {
            var right = BakedRight[i];
            SetSlotEnabledRight(i, true);
            SetSlotColorRight(i, right.ParentPort.Color);
            SetSlotTypeRight(i, right.ParentPort.Type);
        }

        var leftCount = BakedLeft.Count;
        var rightCount = BakedRight.Count;
        var max = Math.Max(leftCount, rightCount);
        
        var leftContainers = new List<Control>();
        var rightContainers = new List<Control>();
        
        for (var i = 0; i < max; i++)
        {
            var hbox = new HBoxContainer();
            var leftContainer = new HBoxContainer();
            var rightContainer = new HBoxContainer();
            
            leftContainers.Add(leftContainer);
            rightContainers.Add(rightContainer);
            
            hbox.AddChild(leftContainer);
            hbox.AddChild(new VSeparator());
            hbox.AddChild(rightContainer);
            leftContainer.Alignment = BoxContainer.AlignmentMode.Begin;
            rightContainer.Alignment = BoxContainer.AlignmentMode.End;
            leftContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rightContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            
            if (i < leftCount)
            {
                var left = BakedLeft[i];
                var label = new Label();
                label.Text = left.Index is null ? left.ParentPort.Name : $"{left.ParentPort.Name}[{left.Index.Value}]";
                label.MouseFilter = MouseFilterEnum.Pass;
                leftContainer.AddChild(label);

                var hoverText = left.ParentPort.ReferenceType is null ? TypeMap.GetTypeName(left.ParentPort.Type) : $"Reference<{left.ParentPort.ReferenceType.GetNiceTypeName()}>";

                leftContainer.MouseEntered += () =>
                {
                    EditorRoot.Instance.HoverText = hoverText;
                };
                leftContainer.MouseExited += () =>
                {
                    if (EditorRoot.Instance.HoverText == hoverText) EditorRoot.Instance.HoverText = "";
                };
                
                if (left.Index is 0)
                {
                    //create buttons
                    var addButton = new Button();
                    addButton.Text = "+";
                    var owner = left.ParentPort;
                    addButton.Pressed += () =>
                    {
                        owner.Count += 1;
                        Generate(nukeRight: false);
                    };
                    var subButton = new Button();
                    subButton.Text = "-";
                    subButton.Pressed += () =>
                    {
                        owner.Count -= 1;
                        Generate(nukeRight: false);
                    };
                    subButton.Disabled = owner.Count == 1;
                    leftContainer.AddChild(addButton);
                    leftContainer.AddChild(subButton);
                }
            }
            if (i < rightCount)
            {
                var right = BakedRight[i];
                var label = new Label();
                label.Text = right.Index is null ? right.ParentPort.Name : $"{right.ParentPort.Name}[{right.Index.Value}]";
                label.MouseFilter = MouseFilterEnum.Pass;
                
                var hoverText = TypeMap.GetTypeName(right.ParentPort.Type);

                rightContainer.MouseEntered += () =>
                {
                    EditorRoot.Instance.HoverText = hoverText;
                };
                rightContainer.MouseExited += () =>
                {
                    if (EditorRoot.Instance.HoverText == hoverText) EditorRoot.Instance.HoverText = "";
                };
                
                if (right.Index is 0)
                {
                    //create buttons
                    var addButton = new Button();
                    addButton.Text = "+";
                    var owner = right.ParentPort;
                    addButton.Pressed += () =>
                    {
                        owner.Count += 1;
                        Generate(nukeLeft: false);
                    };
                    var subButton = new Button();
                    subButton.Text = "-";
                    subButton.Pressed += () =>
                    {
                        owner.Count -= 1;
                        Generate(nukeLeft: false);
                    };
                    subButton.Disabled = owner.Count == 1;
                    rightContainer.AddChild(addButton);
                    rightContainer.AddChild(subButton);
                }
                rightContainer.AddChild(label);
            }
            AddChild(hbox);
            ChildrenItems.Add(hbox);
        }

        var leftMax = leftContainers.Max(i => i.GetMinimumSize().X);
        var rightMax = rightContainers.Max(i => i.GetMinimumSize().X);

        var leftSize = new Vector2(leftMax, 0);
        var rightSize = new Vector2(rightMax, 0);
        
        foreach (var left in leftContainers) left.CustomMinimumSize = leftSize;
        foreach (var right in rightContainers) right.CustomMinimumSize = rightSize;

        foreach (var globalRef in GlobalRefInfos)
        {
            var editor = GlobalRefEditor.Create(globalRef.Value, globalRef.Type, globalRef.Name, globalRef.Color);
            var hoverText = globalRef.Type.GetNiceTypeName();
            editor.NameContainer.MouseEntered += () =>
            {
                EditorRoot.Instance.HoverText = hoverText;
            };
            editor.NameContainer.MouseExited += () =>
            {
                if (EditorRoot.Instance.HoverText == hoverText) EditorRoot.Instance.HoverText = "";
            };
            AddChild(editor);
        }

        foreach (var extra in Extra)
        {
            var editor = ExtraDataEditor.Create(extra);
            var hoverText = extra.Type.GetNiceTypeName();
            editor.NameContainer.MouseEntered += () =>
            {
                EditorRoot.Instance.HoverText = hoverText;
            };
            editor.NameContainer.MouseExited += () =>
            {
                if (EditorRoot.Instance.HoverText == hoverText) EditorRoot.Instance.HoverText = "";
            };
            AddChild(editor);
        }
    }

    public override void _Ready()
    {
        base._Ready();
        Generate();
    }
}