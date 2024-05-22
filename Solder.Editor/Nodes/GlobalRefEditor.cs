using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Solder.Editor.Nodes;

public partial class GlobalRefEditor : PanelContainer
{
    private static readonly PackedScene Scene = GD.Load<PackedScene>("res://GlobalRefEditor.tscn");
    [Export] public Label NameLabel { get; private set; }
    [Export] public PanelContainer NameContainer { get; private set; }
    [Export] public CheckBox DriveCheckBox { get; private set; }
    [Export] public Control ValueEditParent { get; private set; }
    public ValueEdit Edit;
    public OptionButton Options;
    
    public GlobalRefValue RootValue;
    public Type Type;

    public static GlobalRefEditor Create(GlobalRefValue value, Type type, string name, Color color)
    {
        var node = Scene.Instantiate<GlobalRefEditor>();
        node.RootValue = value;
        node.NameLabel.Text = name;
        node.Type = type;
        node.NameContainer.SelfModulate = color;
        node.DriveCheckBox.ButtonPressed = value.Drive;
        node.DriveCheckBox.Toggled += node.DriveCheckBoxOnToggled;
        node.CreateEditor();
        return node;
    }
    private void CreateEditor()
    {
        if (!DriveCheckBox.ButtonPressed && (ValueEdit.SupportedDedicatedEditors.Contains(Type) || Type.IsEnum))
        {
            Edit = ValueEdit.Create(Type);
            
            Edit.Deserialize(RootValue.Value);
            Edit.Changed += () =>
            {
                RootValue.Value = Edit.Serialize();
            };
            ValueEditParent.AddChild(Edit);
        }
        else
        {
            var currentIndex = int.Parse(RootValue.Value);
            
            var map = EditorRoot.Instance.ImportNameMap;
            if (!map.TryGetValue(Type, out var list))
            {
                var l = new List<string>();
                map.Add(Type, l);
                for (var i = 0; i <= currentIndex; i++)
                {
                    l.Add(i.ToString());
                }
                EditorRoot.Instance.TypeNameMap.Regenerate();
            }
            else
            {
                if (list.Count <= currentIndex)
                {
                    for (var i = list.Count; i <= currentIndex; i++)
                    {
                        list.Add(i.ToString());
                    }
                    EditorRoot.Instance.TypeNameMap.Regenerate();
                }
            }
            var dropDown = new OptionButton();
            Options = dropDown;
            dropDown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            dropDown.ItemSelected += index => RootValue.Value = index.ToString();
            PopulateDropDown(currentIndex);
            ValueEditParent.AddChild(dropDown);
        }
    }
    public void PopulateDropDown(int currentIndex)
    {
        if (Options is null) return;
        var names = EditorRoot.Instance.ImportNameMap[Type];
        Options.Clear();
        foreach (var n in names) Options.AddItem(n);
        Options.Selected = Math.Min(currentIndex, names.Count - 1);
        if (currentIndex != Options.Selected) Options.EmitSignal(OptionButton.SignalName.ItemSelected);
    }
    private void DriveCheckBoxOnToggled(bool on)
    {
        RootValue.Drive = on;
        RootValue.Value = RootValue.Drive ? "0" : ValueEdit.Default(Type);
        foreach (var c in ValueEditParent.GetChildren())
        {
            ValueEditParent.RemoveChild(c);
            c.QueueFree();
        }
        Edit = null;
        Options = null;
        CreateEditor();
        EditorRoot.Instance.TypeNameMap.Regenerate();
    }
}