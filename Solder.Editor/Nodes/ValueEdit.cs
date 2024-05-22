using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Elements.Core;
using Godot;

namespace Solder.Editor.Nodes;

public partial class ValueEdit : PanelContainer
{
    public delegate void ValueEditUpdated();
    public static readonly Type[] IntegerTypes =
    {
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong)
    };
    public static readonly Type[] FloatingPointTypes =
    {
        typeof(float),
        typeof(double),
        typeof(decimal),
    };
    public static readonly Type[] NumberTypes = IntegerTypes.Concat(FloatingPointTypes).ToArray();

    public static readonly Type[] MatrixTypes =
    {
        typeof (float2x2),
        typeof (double2x2),
        typeof (float3x3),
        typeof (double3x3),
        typeof (float4x4),
        typeof (double4x4),
    };

    public static readonly Type[] BoolTypes =
    {
        typeof(bool),
        typeof(bool2),
        typeof(bool3),
        typeof(bool4),
    };

    public static readonly Type[] SupportedNumberSpinboxes =
    {
        typeof (byte),
        typeof (ushort),
        typeof (uint),
        typeof (ulong),
        typeof (sbyte),
        typeof (short),
        typeof (int),
        typeof (long),
        typeof (float),
        typeof (double),
        typeof (decimal),
        typeof (uint2),
        typeof (ulong2),
        typeof (int2),
        typeof (long2),
        typeof (float2),
        typeof (double2),
        typeof (uint3),
        typeof (ulong3),
        typeof (int3),
        typeof (long3),
        typeof (float3),
        typeof (double3),
        typeof (uint4),
        typeof (ulong4),
        typeof (int4),
        typeof (long4),
        typeof (float4),
        typeof (double4),
        typeof (float2x2),
        typeof (double2x2),
        typeof (float3x3),
        typeof (double3x3),
        typeof (float4x4),
        typeof (double4x4),
        typeof (floatQ),
        typeof (doubleQ),
    };

    public static readonly Type[] SupportedDedicatedEditors = SupportedNumberSpinboxes.Concat(BoolTypes).Concat(new []
    {
        typeof (char),
        typeof (string),
        typeof (Uri),
        //typeof (DateTime), //TODO: idk???
        //typeof (TimeSpan),
        typeof(color),
        typeof (colorX)
    }).ToArray();
    
    public static ValueEdit Create(Type t)
    {
        var editor = new ValueEdit();
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        editor.Set("theme_override_styles/panel", new StyleBoxEmpty());
        if (SupportedDedicatedEditors.Contains(t) || t.IsEnum) editor.CreateDedicatedEditor(t);
        return editor;
    }

    public event ValueEditUpdated Changed = () => { };

    public Action<string> Deserialize { get; private set; }
    public Func<string> Serialize { get; private set; }

    public static string Default(Type t)
    {
        var val = "0";
        if (SupportedDedicatedEditors.Contains(t))
        {
            if (t == typeof(string)) return "";
            val = t.GetDefault().ToString();
        }
        else if (t.IsEnum) val = Enum.GetNames(t).First();
        return val;
    }
    private void CreateDedicatedEditor(Type t)
    {
        if (t.IsEnum)
        {
            var option = CreateEnumOptionButton(t);
            AddChild(option);
            Serialize = () => option.GetItemText(option.Selected);
            Deserialize = str =>
            {
                for(var i = 0; i < option.ItemCount; i++)
                {
                    if (option.GetItemText(i) != str) continue;
                    option.Selected = i;
                    return;
                }
            };
        }
        else if (BoolTypes.Contains(t)) CreateBoolCheckboxes(t);
        else if (SupportedNumberSpinboxes.Contains(t)) CreateNumberSpinboxes(t);
        else if (t == typeof(string) || t == typeof(Uri))
        {
            var edit = new TextEdit();
            edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            edit.TextChanged += () => Changed();
            edit.CustomMinimumSize = new Vector2(128, 0);
            edit.WrapMode = TextEdit.LineWrappingMode.Boundary;
            edit.ScrollFitContentHeight = true;
            AddChild(edit);
            Serialize = () => edit.Text;
            Deserialize = str => edit.Text = str;
        }
        else if (t == typeof(char))
        {
            var edit = new LineEdit();
            edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            edit.TextChanged += _ => Changed();
            AddChild(edit);
            edit.MaxLength = 1;
            Serialize = () => string.IsNullOrWhiteSpace(edit.Text) ? ((char)0).ToString() : edit.Text;
            Deserialize = str => edit.Text = str;
        }
        else if (t == typeof(colorX) || t == typeof(color))
        {
            var boxes = new List<SpinBox>();
            for (var b = 0; b < 4; b++) boxes.Add(CreateSpinbox(typeof(float)));

            var vBox = new VBoxContainer();
            AddChild(vBox);
            vBox.SizeFlagsVertical = SizeFlags.ExpandFill;
            
            var hBox = new HBoxContainer();
            vBox.AddChild(hBox);

            var colorBoxContainer = new HBoxContainer();
            vBox.AddChild(colorBoxContainer);
            colorBoxContainer.CustomMinimumSize = new Vector2(0, 16);
            colorBoxContainer.SizeFlagsVertical = SizeFlags.ExpandFill;

            var colorBox = new PanelContainer();
            colorBox.Set("theme_override_styles/panel", new StyleBoxFlat { BgColor = Colors.White });
            colorBoxContainer.AddChild(colorBox);
            colorBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var colorBoxOpaque = new PanelContainer();
            colorBoxOpaque.Set("theme_override_styles/panel", new StyleBoxFlat { BgColor = Colors.White });
            colorBoxContainer.AddChild(colorBoxOpaque);
            colorBoxOpaque.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            //TODO: this doesnt work on init
            Changed += () =>
            {
                colorBox.SelfModulate = new Color((float)boxes[0].Value, (float)boxes[1].Value, (float)boxes[2].Value, (float)boxes[3].Value);
                colorBoxOpaque.SelfModulate = new Color((float)boxes[0].Value, (float)boxes[1].Value, (float)boxes[2].Value);
            };
            
            foreach (var b in boxes)
            {
                hBox.AddChild(b);
                //b.Step = 0.01d;
            }

            if (t == typeof(colorX))
            {
                var option = CreateEnumOptionButton(typeof(ColorProfile));
                hBox.AddChild(option);

                Serialize = () => new colorX(
                        (float)boxes[0].Value, (float)boxes[1].Value, (float)boxes[2].Value, (float)boxes[3].Value,
                        Enum.Parse<ColorProfile>(option.GetItemText(option.Selected)))
                    .ToString();

                Deserialize = str =>
                {
                    var color = colorX.Parse(str);
                    boxes[0].Value = color.r;
                    boxes[1].Value = color.g;
                    boxes[2].Value = color.b;
                    boxes[3].Value = color.a;

                    var enumStr = color.profile.ToString();
                    for(var i = 0; i < option.ItemCount; i++)
                    {
                        if (option.GetItemText(i) != enumStr) continue;
                        option.Selected = i;
                        return;
                    }
                };
            }
            else
            {
                Serialize = () =>
                    new color((float)boxes[0].Value, (float)boxes[1].Value, (float)boxes[2].Value,
                        (float)boxes[3].Value).ToString();

                Deserialize = str =>
                {
                    var c = color.Parse(str);
                    boxes[0].Value = c.r;
                    boxes[1].Value = c.g;
                    boxes[2].Value = c.b;
                    boxes[3].Value = c.a;
                };
            }
        }
    }

    private OptionButton CreateEnumOptionButton(Type t)
    {
        var option = new OptionButton();
        var values = Enum.GetNames(t);
        foreach (var name in values) option.AddItem(name);
        option.Selected = 0;
        option.ItemSelected += _ => Changed();
        return option;
    }
    private void CreateBoolCheckboxes(Type t)
    {
        if (t == typeof(bool))
        {
            var check = new CheckBox();
            AddChild(check);
            check.Toggled += _ => Changed();
            Serialize = () => check.ButtonPressed.ToString();
            Deserialize = str => check.ButtonPressed = bool.Parse(str);
        }
        else
        {
            var dimensions = ((IVector)Activator.CreateInstance(t)).Dimensions;

            var boxes = new List<CheckBox>();
            for (var b = 0; b < dimensions; b++) boxes.Add(new CheckBox());
            var hBox = new HBoxContainer();
            AddChild(hBox);
            foreach (var b in boxes)
            {
                hBox.AddChild(b);
                b.Toggled += _ => Changed();
            }
            Serialize = () =>
            {
                var start = boxes.Aggregate("[ ", (current, b) => current + $"{b.ButtonPressed.ToString()}, ");
                start = start[..^2];
                start += " ]";
                return start;
            };
            Deserialize = str =>
            {
                var chopped = str.Replace("[", "").Replace("]", "").Replace(";", " ");
                var split = chopped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var b = 0; b < split.Length; b++) boxes[b].ButtonPressed = bool.Parse(split[b]);
            };
        }
    }
    private void CreateNumberSpinboxes(Type t)
    {
        //these will be interpreted as euler angles when compiled and will be converted to quaternions
        if (t == typeof(floatQ) || t == typeof(doubleQ))
        {
            var boxes = new List<SpinBox>();

            var itemType = t == typeof(floatQ) ? typeof(float) : typeof(double);
            
            for (var b = 0; b < 3; b++) boxes.Add(CreateSpinbox(itemType));
            var hBox = new HBoxContainer();
            AddChild(hBox);
            foreach (var b in boxes) hBox.AddChild(b);
            Serialize = () => doubleQ.Euler(boxes[0].Value, boxes[1].Value, boxes[2].Value).ToString();
            Deserialize = str =>
            {
                var value = doubleQ.Parse(str).EulerAngles;
                for (var b = 0; b < 3; b++) boxes[b].Value = value[b];
            };
        }
        if (MatrixTypes.Contains(t))
        {
            var size = int.Parse(t.Name.Last().ToString()); //lol
            var isDouble = t.Name.StartsWith("double");
            
            var boxes = new List<SpinBox>();

            var vBox = new VBoxContainer();
            AddChild(vBox);
            for (var y = 0; y < size; y++)
            {
                var hBox = new HBoxContainer();
                vBox.AddChild(hBox);
                for (var x = 0; x < size; x++)
                {
                    var box = CreateSpinbox(isDouble ? typeof(double) : typeof(float));
                    boxes.Add(box);
                    hBox.AddChild(box);
                }
            }
            Serialize = () =>
            {
                var start = boxes.Aggregate("[ ", (current, b) => current + $"{b.Value}; ");
                start = start[..^2];
                start += " ]";
                return start;
            };
            Deserialize = str =>
            {
                var chopped = str.Replace("[", "").Replace("]", "").Replace(";", " ");
                var split = chopped.Split(' ').Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
                for (var b = 0; b < split.Count; b++) boxes[b].Value = double.Parse(split[b]);
            };

            return;
        }
        foreach (var i in NumberTypes)
        {
            if (i == t)
            {
                var box = CreateSpinbox(t);
                AddChild(box);
                
                Serialize = () => box.Value.ToString(CultureInfo.InvariantCulture);
                Deserialize = str => box.Value = double.Parse(str);
                
                return;
            }
            var vecType = typeof(IVector<>).MakeGenericType(i);
            if (t.GetInterfaces().Contains(vecType))
            {
                var dimensions = ((IVector)Activator.CreateInstance(t)).Dimensions;

                var boxes = new List<SpinBox>();
                for (var b = 0; b < dimensions; b++) boxes.Add(CreateSpinbox(i));
                var hBox = new HBoxContainer();
                AddChild(hBox);
                foreach (var b in boxes) hBox.AddChild(b);
                Serialize = () =>
                {
                    var start = boxes.Aggregate("[ ", (current, b) => current + $"{b.Value}; ");
                    start = start[..^2];
                    start += " ]";
                    return start;
                };
                Deserialize = str =>
                {
                    var chopped = str.Replace("[", "").Replace("]", "").Replace(";", " ");
                    var split = chopped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (var b = 0; b < split.Length; b++) boxes[b].Value = double.Parse(split[b]);
                };
                
                return;
            }
        }
    }

    private SpinBox CreateSpinbox(Type t)
    {
        var box = new SpinBox();
        if (IntegerTypes.Contains(t)) box.Rounded = true;
        if (FloatingPointTypes.Contains(t))
        {
            box.Step = 0;
            box.AllowGreater = true;
            box.AllowLesser = true;
            //box.CustomArrowStep = 0.1d;
        }
        if (t == typeof(byte))
        {
            box.MinValue = byte.MinValue;
            box.MaxValue = byte.MaxValue;
        }

        if (t == typeof(sbyte))
        {
            box.MinValue = sbyte.MinValue;
            box.MaxValue = sbyte.MaxValue;
        }

        if (t == typeof(short))
        {
            box.MinValue = short.MinValue;
            box.MaxValue = short.MaxValue;
        }

        if (t == typeof(ushort))
        {
            box.MinValue = ushort.MinValue;
            box.MaxValue = ushort.MaxValue;
        }

        if (t == typeof(int))
        {
            box.MinValue = int.MinValue;
            box.MaxValue = int.MaxValue;
        }

        if (t == typeof(uint))
        {
            box.MinValue = uint.MinValue;
            box.MaxValue = uint.MaxValue;
        }

        if (t == typeof(long))
        {
            box.MinValue = long.MinValue;
            box.MaxValue = long.MaxValue;
        }

        if (t == typeof(ulong))
        {
            box.MinValue = ulong.MinValue;
            box.MaxValue = ulong.MaxValue;
        }
        
        box.ValueChanged += _ => Changed();

        return box;
    }
}