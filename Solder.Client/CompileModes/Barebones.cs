using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Elements.Core;
using FrooxEngine;

namespace Solder.Client.CompileModes;

public class Barebones : BaseCompileMode
{
    public override bool SupportsMonopack => true;
    private static readonly MethodInfo InternalImportReferenceMethod = typeof(Barebones).GetMethod(nameof(InternalImportReference), BindingFlags.Instance | BindingFlags.NonPublic);
    private T InternalImportReference<T>(int index) where T : class, IWorldElement
    {
        var multiplexer = Settings.ImportRoot.GetComponent<ReferenceMultiplexer<T>>();
        if (multiplexer is null) return null;
        if (index >= multiplexer.References.Count) return null;
        return multiplexer.References[index];
    }
    
    private static readonly MethodInfo BarebonesHandleEnsureValueImportMethod =
        typeof(Barebones).GetMethod(nameof(BarebonesHandleEnsureValueImport),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo BarebonesHandleEnsureReferenceImportMethod =
        typeof(Barebones).GetMethod(nameof(BarebonesHandleEnsureReferenceImport),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static void BarebonesHandleEnsureValueImport<T>(Slot root, int count)
    {
        var multiplexer = root.GetComponentOrAttach<ValueMultiplexer<T>>();
        if (multiplexer.Values.Count >= count) return;
        while (multiplexer.Values.Count < count) multiplexer.Values.Add();
    }

    private static void BarebonesHandleEnsureReferenceImport<T>(Slot root, int count) where T : class, IWorldElement
    {
        var multiplexer = root.GetComponentOrAttach<ReferenceMultiplexer<T>>();
        if (multiplexer.References.Count >= count) return;
        while (multiplexer.References.Count < count) multiplexer.References.Add();
    }
    
    public override CompileMode Mode => CompileMode.Barebones;
    public override void GenerateMenu(Slot slot, ContextMenu menu, bool monopack, bool persist)
    {
        var tag = slot.Tag;
        var scriptName = tag.Substring(("Compile(".Length), (tag.Length - 1) - ("Compile(".Length));
        var parsedName = SolderClient.SanitizeString(scriptName);

        if (string.IsNullOrWhiteSpace(parsedName)) return;

        var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");

        if (!File.Exists(findPath)) return;

        var compileMenuItem = menu.AddItem("Compile Script", (Uri)null, colorX.Lime);
        compileMenuItem.Button.LocalPressed += (_, _) => CompileButtonMethod(findPath, this, monopack, persist, slot, slot, slot);

        var initializeMenuItem = menu.AddItem("Initialize", (Uri)null, colorX.Azure);
        initializeMenuItem.Button.LocalPressed += (_, _) =>
        {
            var file = File.ReadAllText(findPath);
            var deserialize = JsonSerializer.Deserialize<SerializedScript>(file);
            foreach (var names in deserialize.ImportNames)
            {
                var type = names.Type.GetType(ResoniteScriptDeserializer.AllTypes);
                var valueType = !type.GetInterfaces().Contains(typeof(IWorldElement));
                var count = names.Names.Count;
                try
                {
                    if (valueType)
                        BarebonesHandleEnsureValueImportMethod.MakeGenericMethod(type).Invoke(null, [slot, count]);
                    else
                        BarebonesHandleEnsureReferenceImportMethod.MakeGenericMethod(type).Invoke(null, [slot, count]);
                }
                catch
                {
                    // ignored
                }
            }
        };
    }
    public override T Import<T>(int index)
    {
        if (!Coder<T>.IsEnginePrimitive) return (T)InternalImportReferenceMethod.MakeGenericMethod(typeof(T)).Invoke(this, [index]);

        var multiplexer = Settings.ImportRoot.GetComponent<ValueMultiplexer<T>>();
        if (multiplexer is null) return default;
        if (index >= multiplexer.Values.Count) return default;
        return multiplexer.Values.GetElement(index);
    }
    public override Sync<T> ImportValue<T>(int index)
    {
        var multiplexer = Settings.ImportRoot.GetComponent<ValueMultiplexer<T>>();
        if (multiplexer is null) return null;
        if (index >= multiplexer.Values.Count) return null;
        return multiplexer.Values.GetElement(index);
    }
    public override SyncRef<T> ImportReference<T>(int index)
    {
        var multiplexer = Settings.ImportRoot.GetComponent<ReferenceMultiplexer<T>>();
        if (multiplexer is null) return null;
        if (index >= multiplexer.References.Count) return null;
        return multiplexer.References.GetElement(index);
    }
    public override void CleanupPreviousCompile(Slot nodeRoot)
    {
        var children = nodeRoot.Children.ToList().Where(c => c.Tag == "Compiled").ToList();
        foreach (var c in children) c.Destroy();
    }
}
