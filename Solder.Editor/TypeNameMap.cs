using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using Godot;
using Solder.Editor.Nodes;

namespace Solder.Editor;

public partial class TypeNameMap : ScrollContainer
{
    [Export] public EditorRoot Root { get; private set; }
    public void Regenerate()
    {
        var scrollPos = ScrollVertical;
        foreach (var c in GetChildren().ToList()) c.QueueFree();
        var map = Root.ImportNameMap;

        var rootChildren = Root.NodeGraph.GetChildren().OfType<ProtofluxNode>().ToList();

        var margins = new MarginContainer();
        AddChild(margins);
        margins.Set("theme_override_constants/margin_left", 4);
        margins.Set("theme_override_constants/margin_top", 4);
        margins.Set("theme_override_constants/margin_right", 4);
        margins.Set("theme_override_constants/margin_bottom", 4);
        margins.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        margins.SizeFlagsVertical = SizeFlags.ExpandFill;
        
        var container = new VBoxContainer();
        margins.AddChild(container);

        if (map.Count == 0)
        {
            var label = new Label();
            label.Text = "No imports yet\nThis menu will populate when nodes with imports are created";
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(label);
            return;
        }
        
        foreach (var (type, list) in map)
        {
            var nodesContain = rootChildren.Any(i =>
                i.GlobalRefInfos.Any(j => j.Type == type) || 
                i.Extra.Any(j => j.Type == type));
            var itemRoot = new VBoxContainer();
            container.AddChild(itemRoot);

            var header = new HBoxContainer();
            itemRoot.AddChild(header);
            
            var editRoot = new VBoxContainer();
            itemRoot.AddChild(editRoot);

            var labelParent = new PanelContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            header.AddChild(labelParent);

            var label = new Label();
            labelParent.AddChild(label);
            label.Text = type.GetNiceName();
            label.HorizontalAlignment = HorizontalAlignment.Center;

            var addButton = new Button();
            addButton.Text = "+";
            
            var subButton = new Button();
            subButton.Text = "-";
            
            subButton.Disabled = list.Count <= 1;
            
            addButton.Pressed += () =>
            {
                list.Add(list.Count.ToString());
                CreateNameEdit(list.Count - 1, editRoot, list, type);
                subButton.Disabled = list.Count <= 1;
                RefreshImportEditors(type);
            };
            subButton.Pressed += () =>
            {
                list.RemoveAt(list.Count - 1);
                var last = editRoot.GetChildren().Last();
                editRoot.RemoveChild(last);
                last.QueueFree();
                subButton.Disabled = list.Count <= 1;
                RefreshImportEditors(type);
            };
            
            header.AddChild(addButton);
            header.AddChild(subButton);
            
            if (!nodesContain)
            {
                var deleteButton = new Button();
                deleteButton.Text = "X";
                header.AddChild(deleteButton);

                deleteButton.Pressed += () =>
                {
                    map.Remove(type);
                    container.RemoveChild(itemRoot);
                    itemRoot.QueueFree();
                };
            }

            for (var i = 0; i < list.Count; i++)
            {
                CreateNameEdit(i, editRoot, list, type);
            }
        }

        ScrollVertical = scrollPos;
        
        return;
        
        void CreateNameEdit(int index, Control parent, List<string> modifyingList, Type type)
        {
            var hBox = new HBoxContainer();
            parent.AddChild(hBox);
            var label = new Label();
            label.Text = $"{index}: ";
            hBox.AddChild(label);
            var lineEdit = new LineEdit();
            hBox.AddChild(lineEdit);
            lineEdit.PlaceholderText = "Import name here...";
            lineEdit.Text = modifyingList[index];
            lineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            lineEdit.TextChanged += text =>
            {
                if (modifyingList.Count > index) modifyingList[index] = text;
            };
            lineEdit.TextSubmitted += _ =>
            {
                RefreshImportEditors(type);
            };
            lineEdit.FocusExited += () =>
            {
                RefreshImportEditors(type);
            };
        }

        void RefreshImportEditors(Type type)
        {
            var nodes = EditorRoot.Instance.NodeGraph.GetChildren().OfType<ProtofluxNode>().ToList();
            var globalRefEditors = nodes.SelectMany(i => i.GetChildren().OfType<GlobalRefEditor>())
                .Where(i => i.Type == type && i.Options is not null);
            foreach (var e in globalRefEditors) e.PopulateDropDown(e.Options.Selected);
            var extraEditors = nodes.SelectMany(i => i.GetChildren().OfType<ExtraDataEditor>())
                .Where(i => i.Type == type && i.Options is not null);
            foreach (var e in extraEditors) e.PopulateDropDown(e.Options.Selected);
        }
    }
}