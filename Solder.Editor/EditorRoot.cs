using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Actions;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Enums;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Async;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Variables;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using Godot;
using ProtoFlux.Core;
using Solder.Editor.Nodes;
using Array = Godot.Collections.Array;
using FileAccess = Godot.FileAccess;
using MouseButton = Godot.MouseButton;
using Node = Godot.Node;

namespace Solder.Editor;

public class Connection
{
    public GraphNode From;
    public int FromPort;
    public GraphNode To;
    public int ToPort;
}

public static class ConnectionHelper
{
    public static List<Connection> GetConnections(this GraphEdit graph) =>
        graph.GetConnectionList().Select(i => new Connection
        {
            //todo: from_node and to_node are actually a bug, make sure to change them to "from" and "to" if we upgrade 
            From = graph.GetChildren().FirstOrDefault(j => j.Name == i["from_node"].AsStringName()) as GraphNode,
            To = graph.GetChildren().FirstOrDefault(j => j.Name == i["to_node"].AsStringName()) as GraphNode,
            FromPort = i["from_port"].AsInt32(),
            ToPort = i["to_port"].AsInt32(),
        }).ToList();

    public static void DisconnectNode(this GraphEdit graph, Connection connection) => graph.DisconnectNode(connection.From.Name, connection.FromPort, connection.To.Name, connection.ToPort);
}
public partial class EditorRoot : Node
{
    public static EditorRoot Instance;
    [Export] public Tree NodeBrowserTree { get; private set; }
    [Export] public LineEdit NodeBrowserSearchBar { get; private set; }
    [Export] public Button CreateCommentButton { get; private set; }
    [Export] public GraphEdit NodeGraph { get; private set; }
    [Export] public Control HoverTextRoot { get; private set; }
    [Export] public Label HoverTextLabel { get; private set; }
    [Export] public Label GenericTypeLabel { get; private set; }
    [Export] public Button GenericTypeCreateButton { get; private set; }
    [Export] public Control GenericTypeRoot { get; private set; }
    [Export] public Control GenericTypePresetRoot { get; private set; }


    [Export] public Label SaveDirectoryLabel { get; private set; }
    [Export] public Button SaveDirectorySetButton { get; private set; }
    [Export] public LineEdit SaveNameEdit { get; private set; }
    [Export] public Button SaveButton { get; private set; }
    [Export] public Button LoadButton { get; private set; }
    
    [Export] public TypeNameMap TypeNameMap { get; private set; }


    [Export] public PanelContainer RightClickRoot { get; private set; }
    [Export] public VBoxContainer RightClickItemRoot { get; private set; }
    
    [Export] public RichTextLabel CreditLabel;

    public string StoredSavePath;

    public readonly List<LineEdit> GenericTypeTextEditList = new();
    public readonly List<Button> GenericTypePresetList = new();
    public Label GenericTypePresetLabel;
    
    public Type CurrentSelectedGenericType;
    public Type LastValidGenericType;

    public readonly Dictionary<Type, List<string>> ImportNameMap = new();
    
    public string HoverText;
    public float HoverAlpha;

    private bool _canHide;

    private StringName _lastFromNode;
    private long _lastFromPort;
    private bool _lastIsOutput;

    private string _copy;
    public override void _Ready()
    {
        base._Ready();
        Instance = this;
        
        //nodebrowser
        NodeBrowserTree.ItemActivated += NodeBrowserTreeOnItemActivated;
        NodeBrowserTree.ItemSelected += NodeBrowserTreeOnItemSelected;
        //NodeBrowserTree.SetColumnExpand(0, true);
        //NodeBrowserTree.SetColumnClipContent(0, false);
        CreateCommentButton.Pressed += () => CreateComment();
        NodeBrowserSearchBar.TextChanged += NodeBrowserSearchBarOnTextChanged;
        
        //initialize nodebrowser
        CreateTreeNodes(null, NodeMaps.NodeCategoryTree);
        
        //nodegraph
        NodeGraph.DeleteNodesRequest += NodeGraphOnDeleteNodesRequest;
        NodeGraph.ConnectionRequest += NodeGraphOnConnectionRequest;
        NodeGraph.DisconnectionRequest += NodeGraphOnDisconnectionRequest;
        NodeGraph.ConnectionDragStarted += NodeGraphOnConnectionDragStarted;
        NodeGraph.ConnectionDragEnded += NodeGraphOnConnectionDragEnded;
        NodeGraph.GuiInput += NodeGraphOnGuiInput;
        NodeGraph.CopyNodesRequest += NodeGraphOnCopyNodesRequest;
        NodeGraph.PasteNodesRequest += NodeGraphOnPasteNodesRequest;
        NodeGraph.ChildEnteredTree += _ => TypeNameMap.CallDeferred(TypeNameMap.MethodName.Regenerate);
        
        //save dialog and generic type picker
        SaveButton.Disabled = true;
        GenericTypeCreateButton.Pressed += GenericTypeCreateButtonOnPressed;
        SaveButton.Pressed += SaveButtonOnPressed;
        LoadButton.Pressed += LoadButtonOnPressed;
        SaveDirectorySetButton.Pressed  += SaveDirectorySetButtonOnPressed;
        
        //right click stuff
        RightClickRoot.Visible = false;
        RightClickRoot.MouseExited += HideRightClickMenu;
        
        UpdateGenericCreation();
        foreach (var all in TypeMap.AllImpulseTypes) NodeGraph.AddValidConnectionType(all, TypeMap.OperationType);
        foreach (var async in TypeMap.AsynchronousImpulseTypes) NodeGraph.AddValidConnectionType(async, TypeMap.AsyncOperationType);
        TypeMap.TypeMapUpdated += TypeMapOnTypeMapUpdated;
        
        CreditLabel.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        
        TypeNameMap.Regenerate();
        
        LoadPath();
    }
    private void NodeGraphOnPasteNodesRequest()
    {
        if (string.IsNullOrWhiteSpace(_copy)) return;
        var deserialize = JsonSerializer.Deserialize<SerializedScript>(_copy, new JsonSerializerOptions()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        NodeGraph.Paste(deserialize);
    }

    private void NodeGraphOnCopyNodesRequest()
    {
        var copy = NodeGraph.Copy();
        var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        _copy = json;
    }

    private void NodeGraphOnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Right && button.Pressed)
            CallDeferred(MethodName.RightClickMenu);
    }

    private void RightClickMenu()
    {
        if (!RightClickRoot.Visible)
        {
            RightClickRoot.Visible = true;
            RightClickRoot.GlobalPosition = RightClickRoot.GetGlobalMousePosition() - Vector2.One * 4;

            CreateRightClickMenuButtonComment();
            
            CreateRightClickMenuButton("Update", typeof(Update));
            CreateRightClickMenuButton("LocalUpdate", typeof(LocalUpdate));
            CreateRightClickMenuButton("Dynamic Receiver", typeof(DynamicImpulseReceiver));
            CreateRightClickMenuButton("Call", typeof(CallInput));
            CreateRightClickMenuButton("Async Call", typeof(AsyncCallInput));
            
            var vBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            RightClickItemRoot.AddChild(vBox);

            var operationButton = CreateRightClickMenuButton("Events", vBox);
            var hBox = new HBoxContainer();
            vBox.AddChild(hBox);

            hBox.Visible = false;

            operationButton.Pressed += () =>
            {
                hBox.Visible = !hBox.Visible;
            };
            
            var mainButtonContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            hBox.AddChild(mainButtonContainer);

            CreateRightClickMenuButton("OnActivated", typeof(OnActivated), mainButtonContainer);
            CreateRightClickMenuButton("OnDeactivated", typeof(OnDeactivated), mainButtonContainer);
            CreateRightClickMenuButton("OnDestroy", typeof(OnDestroy), mainButtonContainer);
            CreateRightClickMenuButton("OnDestroying", typeof(OnDestroying), mainButtonContainer);
            CreateRightClickMenuButton("OnDuplicate", typeof(OnDuplicate), mainButtonContainer);
            CreateRightClickMenuButton("OnLoaded", typeof(OnLoaded), mainButtonContainer);
            CreateRightClickMenuButton("OnPaste", typeof(OnPaste), mainButtonContainer);
            CreateRightClickMenuButton("OnSaving", typeof(OnSaving), mainButtonContainer);
            CreateRightClickMenuButton("OnStart", typeof(OnStart), mainButtonContainer);
        }
    }

    private void HideRightClickMenu()
    {
        if (!RightClickRoot.Visible) return;
        RightClickRoot.Visible = false;
        foreach (var c in RightClickItemRoot.GetChildren().ToList())
        {
            RightClickItemRoot.RemoveChild(c);
            c.QueueFree();
        }
    }

    private void NodeGraphOnConnectionDragStarted(StringName fromnode, long fromport, bool isoutput)
    {
        _lastFromNode = fromnode;
        _lastFromPort = fromport;
        _lastIsOutput = isoutput;
    }
    
    private Button CreateRightClickMenuButton(string name, Control parent = null)
    {
        var button = new Button();
        button.Text = name;
        (parent ?? RightClickItemRoot).AddChild(button);
        button.MouseFilter = Control.MouseFilterEnum.Pass; //this fixes a crash,???
        return button;
    }

    private Button CreateRightClickMenuButton(string name, Type nodeType, Control parent = null)
    {
        var mousePos = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;
        
        var button = CreateRightClickMenuButton(name, parent);

        button.Pressed += ButtonAction;
        
        return button;
        
        void ButtonAction()
        {
            HideRightClickMenu();
            
            var relay = ProtofluxNode.CreateNode(nodeType);
            
            NodeGraph.AddChild(relay);
            
            var nodePos = mousePos;
            if (NodeGraph.SnappingEnabled) nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
            relay.PositionOffset = nodePos;
        }
    }

    private Button CreateRightClickMenuButtonComment()
    {
        var mousePos = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;
        
        var button = CreateRightClickMenuButton("Add Comment");

        button.Pressed += ButtonAction;
        
        return button;
        
        void ButtonAction()
        {
            HideRightClickMenu();
            
            var relay = CreateComment();
            
            var nodePos = mousePos;
            if (NodeGraph.SnappingEnabled) nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
            relay.PositionOffset = nodePos;
        }
    }
    
    private Button CreateRightClickConnectMenuButton(string name, Type nodeType, int isOutIndex = 0, int isInIndex = 0, Control parent = null)
    {
        var mousePos = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;
        var lastFromNode = _lastFromNode;
        var lastFromPort = (int)_lastFromPort;
        var lastIsOutput = _lastIsOutput;

        var button = CreateRightClickMenuButton(name, parent);

        button.Pressed += ButtonAction;
        
        return button;
        
        void ButtonAction()
        {
            HideRightClickMenu();
            
            var relay = ProtofluxNode.CreateNode(nodeType);
            
            NodeGraph.AddChild(relay);
            
            var nodePos = mousePos;
            if (NodeGraph.SnappingEnabled) nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
            relay.PositionOffset = nodePos;
            
            if (lastIsOutput)
                NodeGraphOnConnectionRequest(lastFromNode, lastFromPort, relay.Name, isOutIndex);
            else
                NodeGraphOnConnectionRequest(relay.Name, isInIndex, lastFromNode, lastFromPort);
        }
    }

    private void NodeGraphOnConnectionDragEnded()
    {
        if (!Input.IsMouseButtonPressed(MouseButton.Right)) return; //detect if the connection ended because of a right click
        var node = NodeGraph.GetChildren().FirstOrDefault(i => i.Name == _lastFromNode);
        if (node is not ProtofluxNode flux) return;
        var port = _lastIsOutput ? flux.BakedRight[(int)_lastFromPort] : flux.BakedLeft[(int)_lastFromPort];
        var type = port.ParentPort.Type;
        
        if (type == TypeMap.ReferenceType) return;

        RightClickRoot.Visible = true;
        RightClickRoot.GlobalPosition = RightClickRoot.GetGlobalMousePosition() - Vector2.One * 4;
        
        if (TypeMap.NotStandardType.Contains(type))
        {
            if (TypeMap.AllImpulseRelatedTypes.Contains(type))
            {
                HandleRightClickMenuOperationImpulse(type);
            }
            return;
        }
        HandleRightClickMenuType(TypeMap.FromTypeIndex(type));
    }

    private void HandleRightClickMenuOperationImpulse(int type)
    {
        var mousePos = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;
        var lastFromNode = _lastFromNode;
        var lastFromPort = (int)_lastFromPort;
        var lastIsOutput = _lastIsOutput;
        
        var sync = TypeMap.AllSyncImpulseRelatedTypes.Contains(type);
        
        var impulseRelayType = sync ? typeof(CallRelay) : typeof(AsyncCallRelay);
        CreateRightClickConnectMenuButton("Relay", impulseRelayType);

        if (lastIsOutput)
        {
            CreateRightClickConnectMenuButton("If", typeof(If));
            
            var forType = sync ? typeof(For) : typeof(AsyncFor);
            CreateRightClickConnectMenuButton("For", forType);
            
            var whileType = sync ? typeof(While) : typeof(AsyncWhile);
            CreateRightClickConnectMenuButton("While", whileType);
            
            var rangeLoopType = sync ? typeof(RangeLoopInt) : typeof(AsyncRangeLoopInt);
            CreateRightClickConnectMenuButton("Range Loop", rangeLoopType);
            
            var sequenceType = sync ? typeof(Sequence) : typeof(AsyncSequence);
            CreateRightClickConnectMenuButton("Sequence", sequenceType);
            
            CreateRightClickConnectMenuButton("Multiplex", typeof(ImpulseMultiplexer));
            CreateRightClickConnectMenuButton("Random", typeof(PulseRandom));
            
            CreateRightClickConnectMenuButton("Once Per Frame", typeof(OnePerFrame));
            
            CreateRightClickConnectMenuButton("Start Async", typeof(StartAsyncTask));

            if (!sync)
            {
                CreateRightClickConnectMenuButton("Delay Seconds", typeof(DelaySecondsFloat));
                CreateRightClickConnectMenuButton("Delay Updates", typeof(DelayUpdates));
                CreateRightClickConnectMenuButton("Delay Updates or Seconds", typeof(DelayUpdatesOrSecondsFloat));
            }
        }
        else
        {
            CreateRightClickConnectMenuButton("Demultiplex", typeof(ImpulseDemultiplexer));
            var callInputType = sync ? typeof(CallInput) : typeof(AsyncCallInput);
            CreateRightClickConnectMenuButton("Call Input", callInputType);
        }
    }

    private static readonly MethodInfo HandleRightClickMenuTypeTypedMethod = typeof(EditorRoot)
        .GetMethod(nameof(HandleRightClickMenuTypeTyped), BindingFlags.Instance | BindingFlags.NonPublic);

    private void HandleRightClickMenuType(Type type) => HandleRightClickMenuTypeTypedMethod.MakeGenericMethod(type).Invoke(this, System.Array.Empty<object>());

    private void HandleRightClickMenuTypeTyped<T>()
    {
        var mousePos = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;
        var lastFromNode = _lastFromNode;
        var lastFromPort = (int)_lastFromPort;
        var lastIsOutput = _lastIsOutput;
        var realType = typeof(T);

        var unmanaged = realType.IsUnmanaged();
        var relayBaseType = unmanaged ? typeof(ValueRelay<>) : typeof(ObjectRelay<>);
        var relayType = relayBaseType.MakeGenericType(realType);

        CreateRightClickConnectMenuButton("Relay", relayType);

        if (lastIsOutput)
        {
            var writeType = (unmanaged ? typeof(ValueWrite<>) : typeof(ObjectWrite<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("Write", writeType, 1);
            var dynamicWriteType = (unmanaged ? typeof(WriteDynamicValueVariable<>) : typeof(WriteDynamicObjectVariable<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("Dynamic Write", dynamicWriteType, 3);

            Type driveType;
            if (unmanaged) 
                driveType = typeof(ValueFieldDrive<>).MakeGenericType(realType);
            else if (realType.GetInterfaces().Contains(typeof(IWorldElement)))
                driveType = typeof(ReferenceDrive<>).MakeGenericType(realType);
            else
                driveType = typeof(ObjectFieldDrive<T>);
            CreateRightClickConnectMenuButton("Drive", driveType);
            
            var equalsType = (unmanaged ? typeof(ValueEquals<>) : typeof(ObjectEquals<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("==", equalsType);
            var notEqualsType = (unmanaged ? typeof(ValueNotEquals<>) : typeof(ObjectNotEquals<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("!=", notEqualsType);

            if (!unmanaged)
            {
                CreateRightClickConnectMenuButton("Is Null", typeof(IsNull<>).MakeGenericType(realType));
                CreateRightClickConnectMenuButton("Not Null", typeof(NotNull<>).MakeGenericType(realType));
            }
            
            if (realType == typeof(bool))
            {
                CreateRightClickConnectMenuButton("Fire On True", typeof(FireOnTrue), 1);
                CreateRightClickConnectMenuButton("Fire On False", typeof(FireOnTrue), 1);
                CreateRightClickConnectMenuButton("Fire While True", typeof(FireWhileTrue));
                
                CreateRightClickConnectMenuButton("Local Fire On True", typeof(FireOnLocalTrue));
                CreateRightClickConnectMenuButton("Local Fire On False", typeof(FireOnLocalTrue));
                CreateRightClickConnectMenuButton("Local Fire While True", typeof(LocalFireWhileTrue));
            }
        }
        else
        {
            //TODO: DataModelObjectAssetRefStore
            Type dataModelStoreType;
            if (realType == typeof(Type)) 
                dataModelStoreType = typeof(DataModelTypeStore);
            else if (realType == typeof(User)) 
                dataModelStoreType = typeof(DataModelUserRefStore);
            else if (realType.GetInterfaces().Contains(typeof(IWorldElement)))
                dataModelStoreType = typeof(DataModelObjectRefStore<>).MakeGenericType(realType);
            else if (unmanaged)
                dataModelStoreType = typeof(DataModelValueFieldStore<>).MakeGenericType(realType);
            else
                dataModelStoreType = typeof(DataModelObjectFieldStore<>).MakeGenericType(realType);

            CreateRightClickConnectMenuButton("Data Model Store", dataModelStoreType);
            
            var storeType = (unmanaged ? typeof(StoredValue<>) : typeof(StoredObject<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("Store", storeType);
            
            var localType = (unmanaged ? typeof(LocalValue<>) : typeof(LocalObject<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("Local", localType);

            if (ValueEdit.SupportedDedicatedEditors.Contains(realType) || realType.IsEnum)
            {
                var inputType = (unmanaged ? typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<>) : typeof(ValueObjectInput<>)).MakeGenericType(realType);
                CreateRightClickConnectMenuButton("Input", inputType);
            }

            Type sourceType = null;
            if (realType == typeof(SyncRef<Slot>)) 
                sourceType = typeof(SlotRefSource);
            else if (realType == typeof(Slot))
                sourceType = typeof(SlotSource);
            else if (realType == typeof(UserRef))
                sourceType = typeof(UserRefSource);
            else if (unmanaged)
                sourceType = typeof(ValueSource<>).MakeGenericType(realType);
            else if (Coder<T>.IsSupported || realType == typeof(Type))
                sourceType = typeof(ObjectValueSource<>).MakeGenericType(realType);
            else if (realType.GetInterfaces().Contains(typeof(IWorldElement)))
                sourceType = typeof(ElementSource<>).MakeGenericType(realType);
            if (sourceType is not null) 
                CreateRightClickConnectMenuButton("Source", sourceType);

            if (realType.GetInterfaces().Contains(typeof(IWorldElement))) 
                CreateRightClickConnectMenuButton("RefSource", typeof(ReferenceSource<>).MakeGenericType(realType));
            
            var dynamicInputType = (unmanaged ? typeof(DynamicVariableValueInput<>) : typeof(DynamicVariableObjectInput<>)).MakeGenericType(realType);
            CreateRightClickConnectMenuButton("Dynamic Input", dynamicInputType);
        }
        
        if (realType.IsEnum)
        {
            var vBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            RightClickItemRoot.AddChild(vBox);

            var operationButton = CreateRightClickMenuButton("Operations", vBox);
            var hBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vBox.AddChild(hBox);

            hBox.Visible = false;

            operationButton.Pressed += () =>
            {
                hBox.Visible = !hBox.Visible;
            };
            
            var mainButtonContainer = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            hBox.AddChild(mainButtonContainer);

            var operators = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var conversion = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            
            mainButtonContainer.AddChild(operators);
            mainButtonContainer.AddChild(conversion);
            
            CreateRightClickConnectMenuButton("Next Value", typeof(NextValue<>).MakeGenericType(realType), parent: operators);
            CreateRightClickConnectMenuButton("Previous Value", typeof(PreviousValue<>).MakeGenericType(realType), parent: operators);
            CreateRightClickConnectMenuButton("Shift Enum", typeof(ShiftEnum<>).MakeGenericType(realType), parent: operators);

            var underlying = Enum.GetUnderlyingType(realType);
            
            if (lastIsOutput)
            {
                if (underlying == typeof(byte))
                    CreateRightClickConnectMenuButton("To Byte", typeof(EnumToByte<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(int))
                    CreateRightClickConnectMenuButton("To Int", typeof(EnumToInt<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(long))
                    CreateRightClickConnectMenuButton("To Long", typeof(EnumToLong<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(short))
                    CreateRightClickConnectMenuButton("To Short", typeof(EnumToShort<>).MakeGenericType(realType),
                        parent: conversion);
                
                else if (underlying == typeof(sbyte))
                    CreateRightClickConnectMenuButton("To SByte", typeof(EnumToSbyte<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(uint))
                    CreateRightClickConnectMenuButton("To UInt", typeof(EnumToUint<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(ulong))
                    CreateRightClickConnectMenuButton("To ULong", typeof(EnumToUlong<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(ushort))
                    CreateRightClickConnectMenuButton("To UShort", typeof(EnumToUshort<>).MakeGenericType(realType),
                        parent: conversion);
            }
            else
            {
                if (underlying == typeof(byte))
                    CreateRightClickConnectMenuButton("From Byte", typeof(ByteToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(int))
                    CreateRightClickConnectMenuButton("From Int", typeof(IntToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(long))
                    CreateRightClickConnectMenuButton("From Long", typeof(LongToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(short))
                    CreateRightClickConnectMenuButton("From Short", typeof(ShortToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                
                else if (underlying == typeof(sbyte))
                    CreateRightClickConnectMenuButton("From SByte", typeof(SbyteToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(uint))
                    CreateRightClickConnectMenuButton("From UInt", typeof(UintToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(ulong))
                    CreateRightClickConnectMenuButton("From ULong", typeof(UlongToEnum<>).MakeGenericType(realType),
                        parent: conversion);
                else if (underlying == typeof(ushort))
                    CreateRightClickConnectMenuButton("From UShort", typeof(UshortToEnum<>).MakeGenericType(realType),
                        parent: conversion);
            }
        }

        if (Coder<T>.IsSupported && unmanaged &&
            ( //Coder<T>.SupportsApproximateComparison ||
                //Coder<T>.SupportsComparison ||
                //Coder<T>.SupportsDistance ||
                Coder<T>.SupportsAddSub ||
                Coder<T>.SupportsNegate ||
                Coder<T>.SupportsMul ||
                Coder<T>.SupportsDiv ||
                Coder<T>.SupportsMod ||
                Coder<T>.SupportsMinMax ||
                Coder<T>.SupportsAbs ||
                Coder<T>.SupportsLerp ||
                Coder<T>.SupportsInverseLerp ||
                Coder<T>.SupportsConstantLerp ||
                Coder<T>.SupportsSmoothLerp ||
                Coder<T>.SupportsRepeat ||
                FakeCoder<T>.SupportsStandardBooleanOperations ||
                FakeCoder<T>.SupportsBooleanShifting ||
                FakeCoder<T>.SupportsBooleanRotation))
        {
            var vBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            RightClickItemRoot.AddChild(vBox);

            var operationButton = CreateRightClickMenuButton("Operations", vBox);
            var hBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vBox.AddChild(hBox);

            hBox.Visible = false;

            operationButton.Pressed += () =>
            {
                hBox.Visible = !hBox.Visible;
            };
            
            var mainButtonContainer = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            hBox.AddChild(mainButtonContainer);

            var operators = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var math = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var boolean = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            
            mainButtonContainer.AddChild(operators);
            mainButtonContainer.AddChild(math);
            mainButtonContainer.AddChild(boolean);
            
            if (Coder<T>.SupportsAddSub)
            {
                CreateRightClickConnectMenuButton("+", typeof(ValueAdd<>).MakeGenericType(realType), parent: operators);
                CreateRightClickConnectMenuButton("-", typeof(ValueSub<>).MakeGenericType(realType), parent: operators);
            }
            if (Coder<T>.SupportsMul)
                CreateRightClickConnectMenuButton("*", typeof(ValueMul<>).MakeGenericType(realType), parent: operators);
            if (Coder<T>.SupportsDiv) 
                CreateRightClickConnectMenuButton("/", typeof(ValueDiv<>).MakeGenericType(realType), parent: operators);
            if (Coder<T>.SupportsMod) 
                CreateRightClickConnectMenuButton("%", typeof(ValueMod<>).MakeGenericType(realType), parent: operators);

            if (lastIsOutput)
            {
                if (FakeCoder<T>.SupportsComparison)
                {
                    CreateRightClickConnectMenuButton(">", FakeCoder<T>.GreaterThanNode, parent: operators);
                    CreateRightClickConnectMenuButton("\u2265", FakeCoder<T>.GreaterOrEqualNode, parent: operators);
                    CreateRightClickConnectMenuButton("<", FakeCoder<T>.LessThanNode, parent: operators);
                    CreateRightClickConnectMenuButton("\u2264", FakeCoder<T>.LessOrEqualNode, parent: operators);
                }
            }
            
            if (Coder<T>.SupportsAbs) 
                CreateRightClickConnectMenuButton("Abs", typeof(ValueAbs<>).MakeGenericType(realType), parent: math);
            if (Coder<T>.SupportsMinMax)
            {
                CreateRightClickConnectMenuButton("Min", typeof(ValueMin<>).MakeGenericType(realType), parent: math);
                CreateRightClickConnectMenuButton("Max", typeof(ValueMax<>).MakeGenericType(realType), parent: math);
            }
            if (Coder<T>.SupportsLerp) 
                CreateRightClickConnectMenuButton("Lerp", typeof(ValueLerp<>).MakeGenericType(realType), parent: math);
            if (Coder<T>.SupportsInverseLerp) 
                CreateRightClickConnectMenuButton("Inverse Lerp", typeof(ValueInverseLerp<>).MakeGenericType(realType), parent: math);
            if (Coder<T>.SupportsConstantLerp) 
                CreateRightClickConnectMenuButton("Constant Lerp", typeof(ValueConstantLerp<>).MakeGenericType(realType), parent: math);
            if (Coder<T>.SupportsSmoothLerp) 
                CreateRightClickConnectMenuButton("Smooth Lerp", typeof(ValueSmoothLerp<>).MakeGenericType(realType), parent: math);
            if (Coder<T>.SupportsRepeat) 
                CreateRightClickConnectMenuButton("Repeat", typeof(ValueRepeat<>).MakeGenericType(realType), parent: math);
            if (FakeCoder<T>.SupportsStandardBooleanOperations)
            {
                CreateRightClickConnectMenuButton("AND", FakeCoder<T>.ANDNode, parent: boolean);
                CreateRightClickConnectMenuButton("NAND", FakeCoder<T>.NANDNode, parent: boolean);
                CreateRightClickConnectMenuButton("NOR", FakeCoder<T>.NORNode, parent: boolean);
                CreateRightClickConnectMenuButton("NOT", FakeCoder<T>.NOTNode, parent: boolean);
                CreateRightClickConnectMenuButton("OR", FakeCoder<T>.ORNode, parent: boolean);
                CreateRightClickConnectMenuButton("XOR", FakeCoder<T>.XORNode, parent: boolean);
                CreateRightClickConnectMenuButton("XNOR", FakeCoder<T>.XNORNode, parent: boolean);
            }
            if (FakeCoder<T>.SupportsBooleanShifting)
            {
                CreateRightClickConnectMenuButton("Shift Left", FakeCoder<T>.ShiftLeftNode, parent: boolean);
                CreateRightClickConnectMenuButton("Shift Right", FakeCoder<T>.ShiftRightNode, parent: boolean);
            }
            if (FakeCoder<T>.SupportsBooleanRotation)
            {
                CreateRightClickConnectMenuButton("Rotate Left", FakeCoder<T>.RotateLeftNode, parent: boolean);
                CreateRightClickConnectMenuButton("Rotate Right", FakeCoder<T>.RotateRightNode, parent: boolean);
            }
        }
    }

    private void NodeBrowserSearchBarOnTextChanged(string newtext)
    {
        if (string.IsNullOrWhiteSpace(newtext)) ShowRecursive(NodeBrowserTree.GetRoot());
        else SearchRecursive(NodeBrowserTree.GetRoot(), newtext.ToLower());
    }

    private void LoadButtonOnPressed()
    {
        if (!Directory.Exists(StoredSavePath)) return;
        //TODO
        var parsedName = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(SaveNameEdit.Text));
        if (string.IsNullOrWhiteSpace(parsedName)) return;

        var path = Path.Combine(StoredSavePath, $"{parsedName}.pfscript");

        if (!FileAccess.FileExists(path)) return;
        
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var read = file.GetAsText();
        file.Close();
                
        var deserialize = JsonSerializer.Deserialize<SerializedScript>(read, new JsonSerializerOptions()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        this.DeserializeScript(deserialize);
    }

    private void SaveDirectorySetButtonOnPressed()
    {
        var window = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
        };
        window.DirSelected += WindowOnDirSelected;
        GetViewport().AddChild(window);
        window.PopupCentered(Vector2I.One * 512);
    }
    private void WindowOnDirSelected(string dir)
    {
        SavePath(dir);
    }
    private void SavePath(string path)
    {
        var file = FileAccess.Open("user://savePath.config", FileAccess.ModeFlags.Write);
        file.StorePascalString(path);
        file.Close();
        ValidatePath(path);
    }
    private void LoadPath()
    {
        if (!FileAccess.FileExists("user://savePath.config")) return;
        var configFile = FileAccess.Open("user://savePath.config", FileAccess.ModeFlags.Read);
        if (configFile is null) return;
        var path = configFile.GetPascalString();
        configFile.Close();
        ValidatePath(path);
    }

    private void ValidatePath(string path)
    {
        StoredSavePath = path;
        if (Directory.Exists(path))
        {
            SaveButton.Disabled = false;
            SaveDirectoryLabel.Text = path;
        }
        else
        {
            SaveButton.Disabled = true;
            SaveDirectoryLabel.Text = "---";
        }
    }
    private void SaveButtonOnPressed()
    {
        if (Directory.Exists(StoredSavePath))
        {
            //TODO
            var parsedName = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(SaveNameEdit.Text));
            if (!string.IsNullOrWhiteSpace(parsedName))
            {
                var serialize = this.SerializeScript();
                var json = JsonSerializer.Serialize(serialize, new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                
                GD.Print(json);
                
                var file = FileAccess.Open(Path.Combine(StoredSavePath, $"{parsedName}.pfscript"), FileAccess.ModeFlags.Write);
                file.StoreString(json);
                file.Close();
                
                var deserialize = JsonSerializer.Deserialize<SerializedScript>(json, new JsonSerializerOptions()
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                this.DeserializeScript(deserialize);
            }
        }
    }

    private void TypeMapOnTypeMapUpdated(Type type)
    {
        var types = TypeMap.CurrentTypeMap.ToList();
        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (type.CastNode(t) is not null) NodeGraph.AddValidConnectionType(types.Count, i);
            if (t.CastNode(type) is not null) NodeGraph.AddValidConnectionType(i, types.Count);
        }
    }

    private void GenericTypeCreateButtonOnPressed() => CreateNode(LastValidGenericType);

    private static bool SearchRecursive(TreeItem item, string text)
    {
        item.Visible = false;
        var has = item.GetText(0).ToLower().Contains(text);
        if (has)
        {
            item.Visible = true;
        }
        var results = item.GetChildren().Select(i => SearchRecursive(i, text)).ToList();
        if (results.Any(i => i) || has)
        {
            item.Visible = true;
            return true;
        }
        var parent = item.GetParent();
        
        if (parent is null) return false;
        if (parent.Visible && !string.IsNullOrWhiteSpace(parent.GetMetadata(0).AsString()))
        {
            item.Visible = true;
            return false;
        }
        
        return false;
    }
    
    private static void ShowRecursive(TreeItem item)
    {
        item.Visible = true;
        foreach (var c in item.GetChildren()) ShowRecursive(c);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var deltaf = (float)delta;
        HoverTextLabel.Text = HoverText;
        var hoverAlphaTarget = string.IsNullOrWhiteSpace(HoverText) ? 0 : 1;
        HoverAlpha = Mathf.MoveToward(HoverAlpha, hoverAlphaTarget, 4 * deltaf);
        HoverTextRoot.Modulate = new Color(1, 1, 1, HoverAlpha);
        HoverTextRoot.GlobalPosition = HoverTextRoot.GetViewport().GetMousePosition();
        HoverTextRoot.ResetSize();
        _canHide = true;
        
        /*
        if (Random.Shared.Next(128) == 0)
        {
            GD.Print(NodeBrowserTree.GetMinimumSize());
        }
        */

        //NodeBrowserTree.CustomMinimumSize = new Vector2(NodeBrowserTree.GetColumnWidth(0), 0);
        //NodeBrowserTree.Size
        //NodeBrowserTree.ResetSize();
    }

    private void NodeGraphOnDisconnectionRequest(StringName fromnode, long fromport, StringName tonode, long toport)
    {
        NodeGraph.DisconnectNode(fromnode, (int)fromport, tonode, (int)toport);
    }

    private void NodeGraphOnConnectionRequest(StringName fromnode, long fromport, StringName tonode, long toport)
    {
        var from = NodeGraph.GetChildren().FirstOrDefault(i => i.Name == fromnode);
        var to = NodeGraph.GetChildren().FirstOrDefault(i => i.Name == tonode);

        if (from is ProtofluxNode fromFlux && to is ProtofluxNode toFlux)
        {
            var rightItem = fromFlux.BakedRight[(int)fromport];
            if (rightItem.ParentPort.Flow)
            {
                var connections = NodeGraph.GetConnections();
                var find = connections.Where(i => (i.From.Name == fromnode && i.FromPort == fromport));
                foreach (var f in find) NodeGraph.DisconnectNode(f.From.Name, f.FromPort, f.To.Name, f.ToPort);
            }
            var leftItem = toFlux.BakedLeft[(int)toport];
            if (!leftItem.ParentPort.Flow)
            {
                var connections = NodeGraph.GetConnections();
                var find = connections.Where(i => (i.To.Name == tonode && i.ToPort == toport));
                foreach (var f in find) NodeGraph.DisconnectNode(f.From.Name, f.FromPort, f.To.Name, f.ToPort);
            }

            var fromType = rightItem.ParentPort.Type;
            var toType = leftItem.ParentPort.Type;

            if (TypeMap.NotStandardType.Contains(fromType) || TypeMap.NotStandardType.Contains(toType))
            {
                NodeGraph.ConnectNode(fromnode, (int)fromport, tonode, (int)toport);
                return;
            }

            if (fromType != toType)
            {
                var fromRealType = TypeMap.FromTypeIndex(fromType);
                var toRealType = TypeMap.FromTypeIndex(toType);

                var cast = ProtofluxNode.CreateCastNode(fromRealType, toRealType);

                var pos = (fromFlux.PositionOffset + toFlux.PositionOffset) * 0.5f;
                if (NodeGraph.SnappingEnabled) pos = pos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
                cast.PositionOffset = pos;
                
                NodeGraph.AddChild(cast);

                NodeGraph.ConnectNode(fromnode, (int)fromport, cast.Name, 0);
                NodeGraph.ConnectNode(cast.Name, 0, tonode, (int)toport);
            }
            else
            {
                NodeGraph.ConnectNode(fromnode, (int)fromport, tonode, (int)toport);
            }
        }
    }

    private void NodeGraphOnDeleteNodesRequest(Array nodes)
    {
        var array = ((Variant)nodes).AsGodotArray<StringName>();
        foreach (var c in NodeGraph.GetChildren().Where(i => array.Contains(i.Name)))
        {
            var connections = NodeGraph.GetConnections().Where(i => i.From == c || i.To == c);
            foreach (var a in connections) NodeGraph.DisconnectNode(a);
            NodeGraph.RemoveChild(c);
            c.QueueFree();
        }
        TypeNameMap.CallDeferred(TypeNameMap.MethodName.Regenerate);
    }

    private void RecalculateTreeSize()
    {
        var font = NodeBrowserTree.GetThemeFont("font");
        var depthSize = NodeBrowserTree.GetThemeConstant("item_margin");
        var maxSize = RecursiveSizeCheck(NodeBrowserTree.GetRoot(), 0) + 32;

        NodeBrowserTree.CustomMinimumSize = new Vector2(maxSize, 0);

        return;

        int RecursiveSizeCheck(TreeItem item, int depth)
        {
            var d = depth * depthSize;
            var textSize = (int)(font.GetStringSize(item.GetText(0)).X * 1.25f);
            if (item.Collapsed) return d + textSize;
            var children = item.GetChildren();
            if (children.Count == 0) return d + textSize;
            return children.Select(i => RecursiveSizeCheck(i, depth + 1)).Append(d + textSize).Max();
        }
    }
    private void NodeBrowserTreeOnItemActivated()
    {
        var selected = NodeBrowserTree.GetSelected();
        if (selected.GetMetadata(0).AsGodotObject() is NodeButtonMetadata metadata)
        {
            if (metadata.Type.IsGenericType)
            {
                StartGenericCreation(metadata.Type);
            }
            else
            {
                CreateNode(metadata.Type);
            }
        }
    }
    private void NodeBrowserTreeOnItemSelected()
    {
        if (!_canHide) return;
        var selected = NodeBrowserTree.GetSelected();
        if (selected.GetMetadata(0).AsGodotObject() is not NodeButtonMetadata)
        {
            _canHide = false;
            //why does this reselect?
            selected.Collapsed = !selected.Collapsed;
            CallDeferred(MethodName.RecalculateTreeSize);
        }
    }
    private void StartGenericCreation(Type type)
    {
        GenericTypeCreateButton.Disabled = true;
        
        GenericTypeLabel.Text = type.GetNiceTypeName();
        CurrentSelectedGenericType = type;

        foreach (var oldEdit in GenericTypeTextEditList) oldEdit.QueueFree();
        foreach (var oldPreset in GenericTypePresetList) oldPreset.QueueFree();
        GenericTypePresetLabel?.QueueFree();
        GenericTypeTextEditList.Clear();
        GenericTypePresetList.Clear();
        GenericTypePresetLabel = null;

        var genericCount = type.GetGenericArguments().Length;

        for (var i = 0; i < genericCount; i++)
        {
            var line = new LineEdit();
            line.PlaceholderText = $"Generic Argument {i + 1}";
            line.TextChanged += _ => UpdateGenericCreation();
            GenericTypeRoot.AddChild(line);
            GenericTypeTextEditList.Add(line);
        }

        if (genericCount == 1)
        {
            var typeLimit = type.GetCustomAttributes().OfType<GenericTypesAttribute>().FirstOrDefault();

            if (typeLimit is null) return;
            
            var typeLimitList = typeLimit.Types.ToList();
            if (!typeLimit.Types.Any()) return;
                
            GenericTypePresetLabel = new Label();
            GenericTypePresetLabel.Text = "Presets";
            GenericTypePresetLabel.HorizontalAlignment = HorizontalAlignment.Center;
            GenericTypeRoot.AddChild(GenericTypePresetLabel);
            foreach (var t in typeLimitList)
            {
                try
                {
                    var make = type.MakeGenericType(t);
                    var button = new Button();
                    var niceName = make.GetNiceTypeName();
                    button.Text = niceName;
                    button.Pressed += () =>
                    {
                        CreateNode(make);
                    };
                    GenericTypePresetRoot.AddChild(button);
                    GenericTypePresetList.Add(button);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private void UpdateGenericCreation()
    {
        if (CurrentSelectedGenericType is null)
        {
            GenericTypeCreateButton.Disabled = true;
            return;
        }

        if (GenericTypeTextEditList.Any(i => string.IsNullOrWhiteSpace(i.Text)))
        {
            GenericTypeCreateButton.Disabled = true;
            return;
        }

        var tryTypes = GenericTypeTextEditList.Select(i => WorkerManager.ParseNiceType(i.Text)).ToArray();
        if (tryTypes.Any(i => i is null))
        {
            GenericTypeCreateButton.Disabled = true;
            return;
        }
        try
        {
            var type = CurrentSelectedGenericType.MakeGenericType(tryTypes);
            var property = type.GetProperty("IsValidGenericInstance", BindingFlags.Public | BindingFlags.Static);
            if (property is not null)
            {
                if ((bool)(property.GetValue(null) ?? false))
                {
                    LastValidGenericType = type;
                    GenericTypeCreateButton.Disabled = false;
                    return;
                }
                GenericTypeCreateButton.Disabled = true;
                return;
            }
            LastValidGenericType = type;
            GenericTypeCreateButton.Disabled = false;
            return;
        }
        catch
        {
            GenericTypeCreateButton.Disabled = true;
            return;
        }
    }
    private ProtofluxNode CreateNode(Type type)
    {
        var node = ProtofluxNode.CreateNode(type);
        NodeGraph.AddChild(node);
        var nodePos = (NodeGraph.ScrollOffset + (NodeGraph.Size * 0.5f)) / NodeGraph.Zoom;

        if (NodeGraph.SnappingEnabled)
        {
            nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
        }
        
        node.PositionOffset = nodePos;
        return node;
    }
    private CommentNode CreateComment()
    {
        var node = new CommentNode();
        NodeGraph.AddChild(node);
        var nodePos = (NodeGraph.ScrollOffset + (NodeGraph.Size * 0.5f)) / NodeGraph.Zoom;

        if (NodeGraph.SnappingEnabled)
        {
            nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
        }
        
        node.PositionOffset = nodePos;
        return node;
    }
    private void CreateTreeNodes(TreeItem parentItem, CategoryMapNode node)
    {
        var item = parentItem is null ? NodeBrowserTree.CreateItem() : parentItem.CreateChild();
        item.SetText(0, node.CurrentNodeName);
        item.Collapsed = true;
        foreach (var subcategory in node.Subcategories)
        {
            CreateTreeNodes(item, subcategory.Value);
        }
        foreach (var n in node.Nodes)
        {
            var parent = item;
            var groupAttribute = n.Type.GetCustomAttributes().OfType<GroupingAttribute>().FirstOrDefault();
            if (groupAttribute is not null)
            {
                var groupName = groupAttribute.GroupName.Split('.').Last();
                var groupParent = item.GetChildren().FirstOrDefault(i => i.GetMetadata(0).AsString() == groupName);
                if (groupParent is null)
                {
                    groupParent = item.CreateChild();
                    groupParent.Collapsed = true;
                    groupParent.SetText(0, groupName);
                    groupParent.SetMetadata(0, groupName);
                    groupParent.SetCustomBgColor(0, Colors.DarkGreen * new Color(0.25f,0.25f,0.25f));
                }
                parent = groupParent;
            }
            var nodeItem = parent.CreateChild();
            nodeItem.SetText(0, n.Type.GetNiceTypeName());
            nodeItem.SetCustomBgColor(0, Colors.DarkGreen);
            nodeItem.SetMetadata(0, new NodeButtonMetadata
            {
                Type = n.Type
            });
        }
        CallDeferred(MethodName.RecalculateTreeSize);
    }
}