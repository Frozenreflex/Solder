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
    
    [Export] public PopupMenu RightClickPopupMenu { get; private set; }
    
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
    private Vector2 _lastRightClickPosition;

    private string _copy;
    
    public static string SanitizeString(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
    }
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
        var tree = NodeMaps.NodeCategoryTree;
        var realRoot = tree["ProtoFlux"]?["Runtimes"]?["Execution"]?["Nodes"];
        
        CreateTreeNodes(null, realRoot ?? tree);
        NodeBrowserTree.GetRoot().Collapsed = false;
        
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
        SaveNameEdit.TextChanged += SaveNameEditOnTextChanged;
        SaveButton.Disabled = true;
        GenericTypeCreateButton.Pressed += GenericTypeCreateButtonOnPressed;
        SaveButton.Pressed += SaveButtonOnPressed;
        LoadButton.Pressed += LoadButtonOnPressed;
        SaveDirectorySetButton.Pressed  += SaveDirectorySetButtonOnPressed;
        
        //right click stuff
        RightClickPopupMenu.IdPressed += id => { PopupMenuOnIdPressed(RightClickPopupMenu, (int)id); };
        
        UpdateGenericCreation();
        foreach (var all in TypeMap.AllImpulseTypes) NodeGraph.AddValidConnectionType(all, TypeMap.OperationType);
        foreach (var async in TypeMap.AsynchronousImpulseTypes) NodeGraph.AddValidConnectionType(async, TypeMap.AsyncOperationType);
        TypeMap.TypeMapUpdated += TypeMapOnTypeMapUpdated;
        
        CreditLabel.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        
        TypeNameMap.Regenerate();
        
        LoadPath();
    }
    private void SaveNameEditOnTextChanged(string newtext)
    {
        var caret = SaveNameEdit.CaretColumn;
        var str = SanitizeString(newtext);
        SaveNameEdit.Text = str;
        SaveNameEdit.CaretColumn = caret;
    }
    private void PopupMenuOnIdPressed(PopupMenu menu, int id)
    {
        menu.GetItemMetadata(id).As<ActionMetadata>()?.Action();
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
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        _copy = json;
    }

    private void NodeGraphOnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Right && button.Pressed)
            CallDeferred(MethodName.DeferRightClickMenu);
    }
    private void DeferRightClickMenu() => CallDeferred(MethodName.RightClickMenu);

    private void SetLastRightClickPosition() => _lastRightClickPosition = (NodeGraph.GetLocalMousePosition() + NodeGraph.ScrollOffset) / NodeGraph.Zoom;

    private void ShowRightClickPopup()
    {
        SetLastRightClickPosition();
        RightClickPopupMenu.Clear();
        foreach (var c in RightClickPopupMenu.GetChildren().OfType<PopupMenu>().ToList())
        {
            RightClickPopupMenu.RemoveChild(c);
            c.QueueFree();
        }

        CallDeferred(MethodName.PopupRightClickMenu);
    }

    private void PopupRightClickMenu()
    {
        var targetPosition = (Vector2I)(NodeGraph.GetGlobalMousePosition() - Vector2.One * 4);
        var targetSize = (Vector2I)RightClickPopupMenu.GetContentsMinimumSize();

        var projectedY = targetPosition.Y + targetSize.Y;
        var globalSize = (int)NodeGraph.GetViewportRect().Size.Y;
        if (projectedY > globalSize) targetPosition = targetPosition with { Y = globalSize - targetSize.Y };
        
        RightClickPopupMenu.Popup(new Rect2I(targetPosition, targetSize));
    }

    private void RightClickMenu()
    {
        if (!RightClickPopupMenu.Visible)
        {
            ShowRightClickPopup();

            AddPopupMenuCommentButton(RightClickPopupMenu);

            AddPopupMenuNodeButton(RightClickPopupMenu, "Update", typeof(Update));
            AddPopupMenuNodeButton(RightClickPopupMenu, "Local Update", typeof(LocalUpdate));
            AddPopupMenuNodeButton(RightClickPopupMenu, "Dynamic Receiver", typeof(DynamicImpulseReceiver));
            AddPopupMenuNodeButton(RightClickPopupMenu, "Call", typeof(CallInput));
            AddPopupMenuNodeButton(RightClickPopupMenu, "Async Call", typeof(AsyncCallInput));

            var events = AddPopupMenuSubmenu(RightClickPopupMenu, "Events");
            
            AddPopupMenuNodeButton(events, "On Activated", typeof(OnActivated));
            AddPopupMenuNodeButton(events, "On Dectivated", typeof(OnDeactivated));
            AddPopupMenuNodeButton(events, "On Destroy", typeof(OnDestroy));
            AddPopupMenuNodeButton(events, "On Destroying", typeof(OnDestroying));
            AddPopupMenuNodeButton(events, "On Duplicate", typeof(OnDuplicate));
            AddPopupMenuNodeButton(events, "On Loaded", typeof(OnLoaded));
            AddPopupMenuNodeButton(events, "On Paste", typeof(OnPaste));
            AddPopupMenuNodeButton(events, "On Saving", typeof(OnSaving));
            AddPopupMenuNodeButton(events, "On Start", typeof(OnStart));
        }
    }

    private void NodeGraphOnConnectionDragStarted(StringName fromnode, long fromport, bool isoutput)
    {
        _lastFromNode = fromnode;
        _lastFromPort = fromport;
        _lastIsOutput = isoutput;
    }
    private PopupMenu AddPopupMenuSubmenu(PopupMenu menu, string name)
    {
        var submenu = new PopupMenu();
        submenu.IdPressed += id => { PopupMenuOnIdPressed(submenu, (int)id); };
        menu.AddChild(submenu);
        submenu.Name = name;
        menu.AddSubmenuItem(name, name);
        return submenu;
    }
    private int AddPopupMenuCommentButton(PopupMenu menu)
    {
        var mousePos = _lastRightClickPosition;
        
        var index = menu.ItemCount;
        menu.AddItem("Add Comment");
        menu.SetItemMetadata(index, new ActionMetadata(ButtonAction));

        return index;
        
        void ButtonAction()
        {
            var relay = CreateComment();
            
            var nodePos = mousePos;
            if (NodeGraph.SnappingEnabled) nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
            relay.PositionOffset = nodePos;
        }
    }

    private int AddPopupMenuNodeButton(PopupMenu menu, string name, Type nodeType)
    {
        var mousePos = _lastRightClickPosition;
        
        var index = menu.ItemCount;
        menu.AddItem(name);
        menu.SetItemMetadata(index, new ActionMetadata(ButtonAction));

        return index;
        
        void ButtonAction()
        {
            var relay = ProtofluxNode.CreateNode(nodeType);
            
            NodeGraph.AddChild(relay);
            
            var nodePos = mousePos;
            if (NodeGraph.SnappingEnabled) nodePos = nodePos.Snapped(Vector2.One * NodeGraph.SnappingDistance);
            relay.PositionOffset = nodePos;
        }
    }
    private int AddPopupMenuConnectButton(PopupMenu menu, string name, Type nodeType, int isOutIndex = 0, int isInIndex = 0)
    {
        var mousePos = _lastRightClickPosition;
        var lastFromNode = _lastFromNode;
        var lastFromPort = (int)_lastFromPort;
        var lastIsOutput = _lastIsOutput;

        var index = menu.ItemCount;
        menu.AddItem(name);
        menu.SetItemMetadata(index, new ActionMetadata(ButtonAction));

        return index;
        
        void ButtonAction()
        {
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

        ShowRightClickPopup();
        
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
        var lastIsOutput = _lastIsOutput;
        var sync = TypeMap.AllSyncImpulseRelatedTypes.Contains(type);
        
        AddPopupMenuConnectButton(RightClickPopupMenu, "Relay", sync ? typeof(CallRelay) : typeof(AsyncCallRelay));

        if (lastIsOutput)
        {
            AddPopupMenuConnectButton(RightClickPopupMenu, "If", typeof(If));
            AddPopupMenuConnectButton(RightClickPopupMenu, "For", sync ? typeof(For) : typeof(AsyncFor));
            AddPopupMenuConnectButton(RightClickPopupMenu, "While", sync ? typeof(While) : typeof(AsyncWhile));
            AddPopupMenuConnectButton(RightClickPopupMenu, "Range Loop", sync ? typeof(RangeLoopInt) : typeof(AsyncRangeLoopInt));
            AddPopupMenuConnectButton(RightClickPopupMenu, "Sequence", sync ? typeof(Sequence) : typeof(AsyncSequence));
            AddPopupMenuConnectButton(RightClickPopupMenu,"Multiplex", typeof(ImpulseMultiplexer));
            AddPopupMenuConnectButton(RightClickPopupMenu,"Random", typeof(PulseRandom));
            AddPopupMenuConnectButton(RightClickPopupMenu,"Once Per Frame", typeof(OnePerFrame));
            AddPopupMenuConnectButton(RightClickPopupMenu,"Start Async", typeof(StartAsyncTask));

            if (!sync)
            {
                AddPopupMenuConnectButton(RightClickPopupMenu,"Delay Seconds", typeof(DelaySecondsFloat));
                AddPopupMenuConnectButton(RightClickPopupMenu,"Delay Updates", typeof(DelayUpdates));
                AddPopupMenuConnectButton(RightClickPopupMenu,"Delay Updates or Seconds", typeof(DelayUpdatesOrSecondsFloat));
            }
        }
        else
        {
            AddPopupMenuConnectButton(RightClickPopupMenu,"Demultiplex", typeof(ImpulseDemultiplexer));
            AddPopupMenuConnectButton(RightClickPopupMenu,"Call", sync ? typeof(CallInput) : typeof(AsyncCallInput));
        }
    }

    private static readonly MethodInfo HandleRightClickMenuTypeTypedMethod = typeof(EditorRoot)
        .GetMethod(nameof(HandleRightClickMenuTypeTyped), BindingFlags.Instance | BindingFlags.NonPublic);

    private void HandleRightClickMenuType(Type type) => HandleRightClickMenuTypeTypedMethod.MakeGenericMethod(type).Invoke(this, System.Array.Empty<object>());

    private void HandleRightClickMenuTypeTyped<T>()
    {
        var lastIsOutput = _lastIsOutput;
        var realType = typeof(T);

        var unmanaged = realType.IsUnmanaged();
        
        AddPopupMenuConnectButton(RightClickPopupMenu,"Relay", (unmanaged ? typeof(ValueRelay<>) : typeof(ObjectRelay<>)).MakeGenericType(realType));

        if (lastIsOutput)
        {
            var writeType = (unmanaged ? typeof(ValueWrite<>) : typeof(ObjectWrite<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Write", writeType, 1);
            var dynamicWriteType = (unmanaged ? typeof(WriteDynamicValueVariable<>) : typeof(WriteDynamicObjectVariable<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Dynamic Write", dynamicWriteType, 3);

            Type driveType;
            if (unmanaged) 
                driveType = typeof(ValueFieldDrive<>).MakeGenericType(realType);
            else if (realType.GetInterfaces().Contains(typeof(IWorldElement)))
                driveType = typeof(ReferenceDrive<>).MakeGenericType(realType);
            else
                driveType = typeof(ObjectFieldDrive<T>);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Drive", driveType);
            
            var equalsType = (unmanaged ? typeof(ValueEquals<>) : typeof(ObjectEquals<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"==", equalsType);
            var notEqualsType = (unmanaged ? typeof(ValueNotEquals<>) : typeof(ObjectNotEquals<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"!=", notEqualsType);

            if (!unmanaged)
            {
                AddPopupMenuConnectButton(RightClickPopupMenu,"Is Null", typeof(IsNull<>).MakeGenericType(realType));
                AddPopupMenuConnectButton(RightClickPopupMenu,"Not Null", typeof(NotNull<>).MakeGenericType(realType));
            }
            
            if (realType == typeof(bool))
            {
                AddPopupMenuConnectButton(RightClickPopupMenu,"Fire On True", typeof(FireOnTrue), 1);
                AddPopupMenuConnectButton(RightClickPopupMenu,"Fire On False", typeof(FireOnFalse), 1);
                AddPopupMenuConnectButton(RightClickPopupMenu,"Fire While True", typeof(FireWhileTrue));
                
                AddPopupMenuConnectButton(RightClickPopupMenu,"Local Fire On True", typeof(FireOnLocalTrue));
                AddPopupMenuConnectButton(RightClickPopupMenu,"Local Fire On False", typeof(FireOnLocalTrue));
                AddPopupMenuConnectButton(RightClickPopupMenu,"Local Fire While True", typeof(LocalFireWhileTrue));
            }
            
            FakeCoderRightClickButton<T>(RightClickPopupMenu, "Unpack");
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

            AddPopupMenuConnectButton(RightClickPopupMenu,"Data Model Store", dataModelStoreType);
            
            var storeType = (unmanaged ? typeof(StoredValue<>) : typeof(StoredObject<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Store", storeType);
            
            var localType = (unmanaged ? typeof(LocalValue<>) : typeof(LocalObject<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Local", localType);

            if (ValueEdit.SupportedDedicatedEditors.Contains(realType) || realType.IsEnum)
            {
                var inputType = (unmanaged ? typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<>) : typeof(ValueObjectInput<>)).MakeGenericType(realType);
                AddPopupMenuConnectButton(RightClickPopupMenu,"Input", inputType);
            }
            if (realType.GetInterfaces().Contains(typeof(IAssetProvider)))
            {
                AddPopupMenuConnectButton(RightClickPopupMenu,"Asset Input", typeof(AssetInput<>).MakeGenericType(realType.GetGenericArguments().First()));
            }
            else
            {
                Type sourceType = null;
                if (realType == typeof(SyncRef<Slot>)) 
                    sourceType = typeof(SlotRefSource);
                /*
                else if (realType == typeof(Slot))
                    sourceType = typeof(SlotSource);
                    */
                //the game doesn't actually use slot source? it spawns an elementsource when you ref a slot, ?????
                else if (realType == typeof(UserRef))
                    sourceType = typeof(UserRefSource);
                else if (unmanaged)
                    sourceType = typeof(ValueSource<>).MakeGenericType(realType);
                else if (Coder<T>.IsSupported || realType == typeof(Type))
                    sourceType = typeof(ObjectValueSource<>).MakeGenericType(realType);
                else if (realType.GetInterfaces().Contains(typeof(IWorldElement)))
                    sourceType = typeof(ElementSource<>).MakeGenericType(realType);
                if (sourceType is not null) 
                    AddPopupMenuConnectButton(RightClickPopupMenu,"Source", sourceType);

                if (realType.GetInterfaces().Contains(typeof(IWorldElement))) 
                    AddPopupMenuConnectButton(RightClickPopupMenu,"RefSource", typeof(ReferenceSource<>).MakeGenericType(realType));
            }
            
            var dynamicInputType = (unmanaged ? typeof(DynamicVariableValueInput<>) : typeof(DynamicVariableObjectInput<>)).MakeGenericType(realType);
            AddPopupMenuConnectButton(RightClickPopupMenu,"Dynamic Input", dynamicInputType);
            
            FakeCoderRightClickButton<T>(RightClickPopupMenu, "Pack");
        }
        
        if (realType.IsEnum)
        {
            var operations = AddPopupMenuSubmenu(RightClickPopupMenu, "Operations");
            
            AddPopupMenuConnectButton(operations, "Next Value", typeof(NextValue<>).MakeGenericType(realType));
            AddPopupMenuConnectButton(operations, "Previous Value", typeof(PreviousValue<>).MakeGenericType(realType));
            AddPopupMenuConnectButton(operations, "Shift Enum", typeof(ShiftEnum<>).MakeGenericType(realType));

            var underlying = Enum.GetUnderlyingType(realType);
            
            if (lastIsOutput)
            {
                if (underlying == typeof(byte))
                    AddPopupMenuConnectButton(operations,"To Byte", typeof(EnumToByte<>).MakeGenericType(realType));
                else if (underlying == typeof(int))
                    AddPopupMenuConnectButton(operations,"To Int", typeof(EnumToInt<>).MakeGenericType(realType));
                else if (underlying == typeof(long))
                    AddPopupMenuConnectButton(operations,"To Long", typeof(EnumToLong<>).MakeGenericType(realType));
                else if (underlying == typeof(short))
                    AddPopupMenuConnectButton(operations,"To Short", typeof(EnumToShort<>).MakeGenericType(realType));
                
                else if (underlying == typeof(sbyte))
                    AddPopupMenuConnectButton(operations,"To SByte", typeof(EnumToSbyte<>).MakeGenericType(realType));
                else if (underlying == typeof(uint))
                    AddPopupMenuConnectButton(operations,"To UInt", typeof(EnumToUint<>).MakeGenericType(realType));
                else if (underlying == typeof(ulong))
                    AddPopupMenuConnectButton(operations,"To ULong", typeof(EnumToUlong<>).MakeGenericType(realType));
                else if (underlying == typeof(ushort))
                    AddPopupMenuConnectButton(operations,"To UShort", typeof(EnumToUshort<>).MakeGenericType(realType));
            }
            else
            {
                if (underlying == typeof(byte))
                    AddPopupMenuConnectButton(operations,"From Byte", typeof(ByteToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(int))
                    AddPopupMenuConnectButton(operations,"From Int", typeof(IntToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(long))
                    AddPopupMenuConnectButton(operations,"From Long", typeof(LongToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(short))
                    AddPopupMenuConnectButton(operations,"From Short", typeof(ShortToEnum<>).MakeGenericType(realType));
                
                else if (underlying == typeof(sbyte))
                    AddPopupMenuConnectButton(operations,"From SByte", typeof(SbyteToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(uint))
                    AddPopupMenuConnectButton(operations,"From UInt", typeof(UintToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(ulong))
                    AddPopupMenuConnectButton(operations,"From ULong", typeof(UlongToEnum<>).MakeGenericType(realType));
                else if (underlying == typeof(ushort))
                    AddPopupMenuConnectButton(operations,"From UShort", typeof(UshortToEnum<>).MakeGenericType(realType));
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
            var operations = AddPopupMenuSubmenu(RightClickPopupMenu, "Operations");
            var operators = AddPopupMenuSubmenu(operations, "Operators");
            var math = AddPopupMenuSubmenu(operations, "Math");
            var interpolation = AddPopupMenuSubmenu(operations, "Interpolation");
            
            if (Coder<T>.SupportsAddSub)
            {
                AddPopupMenuConnectButton(operators, "+", typeof(ValueAdd<>).MakeGenericType(realType));
                AddPopupMenuConnectButton(operators,"-", typeof(ValueSub<>).MakeGenericType(realType));
            }
            if (Coder<T>.SupportsMul)
                AddPopupMenuConnectButton(operators,"*", typeof(ValueMul<>).MakeGenericType(realType));
            if (Coder<T>.SupportsDiv) 
                AddPopupMenuConnectButton(operators,"/", typeof(ValueDiv<>).MakeGenericType(realType));
            if (Coder<T>.SupportsMod) 
                AddPopupMenuConnectButton(operators,"%", typeof(ValueMod<>).MakeGenericType(realType));

            if (lastIsOutput)
            {
                FakeCoderRightClickButton<T>(operators, "GreaterThan", ">");
                FakeCoderRightClickButton<T>(operators, "GreaterOrEqual", "\u2265");
                FakeCoderRightClickButton<T>(operators, "LessThan", "<");
                FakeCoderRightClickButton<T>(operators, "LessOrEqual", "\u2264");
            }
            
            if (Coder<T>.SupportsAbs) 
                AddPopupMenuConnectButton(math,"Abs", typeof(ValueAbs<>).MakeGenericType(realType));
            if (Coder<T>.SupportsMinMax)
            {
                AddPopupMenuConnectButton(math,"Min", typeof(ValueMin<>).MakeGenericType(realType));
                AddPopupMenuConnectButton(math,"Max", typeof(ValueMax<>).MakeGenericType(realType));
            }
            if (Coder<T>.SupportsLerp) 
                AddPopupMenuConnectButton(interpolation,"Lerp", typeof(ValueLerp<>).MakeGenericType(realType));
            if (Coder<T>.SupportsInverseLerp) 
                AddPopupMenuConnectButton(interpolation,"Inverse Lerp", typeof(ValueInverseLerp<>).MakeGenericType(realType));
            if (Coder<T>.SupportsConstantLerp) 
                AddPopupMenuConnectButton(interpolation,"Constant Lerp", typeof(ValueConstantLerp<>).MakeGenericType(realType));
            if (Coder<T>.SupportsSmoothLerp) 
                AddPopupMenuConnectButton(interpolation,"Smooth Lerp", typeof(ValueSmoothLerp<>).MakeGenericType(realType));
            if (Coder<T>.SupportsRepeat) 
                AddPopupMenuConnectButton(interpolation,"Repeat", typeof(ValueRepeat<>).MakeGenericType(realType));
            
            FakeCoderRightClickButton<T>(operators,"AND", "&");
            FakeCoderRightClickButton<T>(operators,"OR", "|");
            FakeCoderRightClickButton<T>(operators,"NOT", "!");
            FakeCoderRightClickButton<T>(operators,"NAND");
            FakeCoderRightClickButton<T>(operators,"NOR");
            FakeCoderRightClickButton<T>(operators,"XOR");
            FakeCoderRightClickButton<T>(operators,"XNOR");
            
            FakeCoderRightClickButton<T>(operators,"ShiftLeft", "<<");
            FakeCoderRightClickButton<T>(operators,"ShiftRight", ">>");
            FakeCoderRightClickButton<T>(operators,"RotateLeft", "\u21ba");
            FakeCoderRightClickButton<T>(operators,"RotateRight", "\u21bb");
        }
    }

    private void FakeCoderRightClickButton<T>(PopupMenu menu, string nodeName, string displayName = null, int isOutIndex = 0, int isInIndex = 0)
    {
        if (FakeCoder<T>.Supports(nodeName, out var nodeType)) AddPopupMenuConnectButton(menu, displayName ?? nodeName, nodeType, isOutIndex, isInIndex);
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
        var parsedName = SaveNameEdit.Text;
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
            var parsedName = SaveNameEdit.Text;
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