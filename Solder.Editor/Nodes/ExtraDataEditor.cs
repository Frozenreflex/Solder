using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Solder.Editor.Nodes;

public partial class ExtraDataEditor : PanelContainer
{
    private static readonly PackedScene Scene = GD.Load<PackedScene>("res://ExtraDataEditor.tscn");
    [Export] public Label NameLabel { get; private set; }
    [Export] public PanelContainer NameContainer { get; private set; }
    [Export] public Control ValueEditParent { get; private set; }
    public ValueEdit Edit;
    public OptionButton Options;
    
    public ExtraData RootData;
    public Type Type;

    public static ExtraDataEditor Create(ExtraData data)
    {
        var node = Scene.Instantiate<ExtraDataEditor>();
        node.RootData = data;
        node.NameLabel.Text = data.Name;
        node.Type = data.Type;
        node.NameContainer.SelfModulate = data.Color;
        node.CreateEditor();
        return node;
    }
    private void CreateEditor()
    {
        if (ValueEdit.SupportedDedicatedEditors.Contains(Type) || Type.IsEnum)
        {
            Edit = ValueEdit.Create(Type);
            
            Edit.Deserialize(RootData.Value);
            Edit.Changed += () =>
            {
                RootData.Value = Edit.Serialize();
            };
            ValueEditParent.AddChild(Edit);
        }
        else
        {
            var currentIndex = int.Parse(RootData.Value);
            
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
            dropDown.ItemSelected += index => RootData.Value = index.ToString();
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
}