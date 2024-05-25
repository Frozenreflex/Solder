using System;
using Godot;

namespace Solder.Editor;

public partial class ActionMetadata : Resource
{
    public Action Action;
    public ActionMetadata(Action action) => Action = action;
}