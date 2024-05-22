using Godot;

namespace Solder.Editor.Nodes;

public partial class CommentNode : GraphNode
{
    public TextEdit Text { get; private set; }
    public override void _Ready()
    {
        base._Ready();
        Text = new TextEdit();
        AddChild(Text);
        Text.PlaceholderText = "Comment goes here...";
        Text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Text.SizeFlagsVertical = SizeFlags.ExpandFill;
        Text.WrapMode = TextEdit.LineWrappingMode.Boundary;
        CustomMinimumSize = new Vector2(256, 32);
        Text.ScrollSmooth = true;
        Text.ScrollFitContentHeight = true;
        Text.TextChanged += ResetSize;
    }
}