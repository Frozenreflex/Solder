using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using HarmonyLib;
using ProtoFlux.Core;
using ResoniteModLoader;

namespace Solder.Client;

public enum CompileMode
{
    Solder,
    //RedprintCube,
    RedprintPack,
    Barebones,
}

public class SolderClient : ResoniteMod
{
    public override string Name => "Solder.Client";
    public override string Author => "Fro Zen";
    public override string Version => "1.2.0";

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> Monopack =
        new("monopack", "Monopack", () => false);
    
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> Persistence =
        new("persistence", "Persistence", () => true);

    internal static ModConfiguration Config;

    public static string ScriptPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
    //public static string SanitizeString(string str) => Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(str));

    public static string SanitizeString(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
    }

    public override void OnEngineInit()
    {
        var harmony = new Harmony("Solder.Client");
        harmony.PatchAll();
        Config = GetConfiguration();
        if (!Directory.Exists(ScriptPath)) Directory.CreateDirectory(ScriptPath);
    }
}

[HarmonyPatch(typeof(ProtoFluxTool))]
public static class ProtoFluxToolPatch
{
    private static bool ValidCompileRootSlot(Slot slot, CompileMode mode)
    {
        var tag = slot.Tag;
        switch (mode)
        {
            case CompileMode.Solder:
            case CompileMode.RedprintPack:
            {
                var parsedName = SolderClient.SanitizeString(slot.Name);
                if (string.IsNullOrWhiteSpace(parsedName)) return false;
                
                var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                
                return File.Exists(findPath);
            }
            case CompileMode.Barebones:
            {
                var scriptName = tag.Substring(("Compile(".Length), (tag.Length - 1) - ("Compile(".Length));
                var parsedName = SolderClient.SanitizeString(scriptName);

                if (string.IsNullOrWhiteSpace(parsedName)) return false;
                
                var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                
                return File.Exists(findPath);
            }
        }
        return false;
    }
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ProtoFluxTool.GenerateMenuItems))]
    public static void GenerateMenuItems(InteractionHandler tool, ContextMenu menu, ProtoFluxTool __instance)
    {
        var grabbed = __instance.GetGrabbedReference();

        //TODO: make this work for compiling multiple scripts in a hierarchy, and also allow overriding the config using tags

        if (grabbed is not Slot slot) return;

        var tag = slot.Tag;

        var mode = (CompileMode)(-1);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (tag.StartsWith("Compile(") && tag.EndsWith(")"))
                mode = CompileMode.Barebones;
            else
                mode = tag switch
                {
                    //"RedprintCube" => CompileMode.RedprintCube,
                    "RedprintPack" => CompileMode.RedprintPack,
                    "pfscript" => CompileMode.Solder,
                    _ => mode,
                };
        }

        if (slot.Children.Any() && slot.Children.Any(i => i.Components.OfType<ProtoFluxNode>().Any()))
        {
            var exportMenuItem = menu.AddItem("Export ProtoFlux", (Uri)null, colorX.Red);
            exportMenuItem.Button.LocalPressed += (_, _) =>
            {
                var serialized = JsonSerializer.Serialize(ResoniteScriptDeserializer.ExportScript(slot), new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
                var parsedName = SolderClient.SanitizeString(slot.Name);
                var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                File.WriteAllText(findPath, serialized);
            };
        }

        if (mode == (CompileMode)(-1) || tag is null) return;
        
        var monopack = SolderClient.Config.GetValue(SolderClient.Monopack);
        var persist = SolderClient.Config.GetValue(SolderClient.Persistence);
        
        switch (mode)
        {
            case CompileMode.Solder:
            {
                var parsedName = SolderClient.SanitizeString(slot.Name);

                if (string.IsNullOrWhiteSpace(parsedName)) break;
                
                var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                
                if (!File.Exists(findPath)) break;
                
                var compileMenuItem = menu.AddItem("Compile Script", (Uri)null, colorX.Lime);
                compileMenuItem.Button.LocalPressed += (_, _) => CompileMethod(findPath, mode, monopack, persist, slot, slot);
                
                var initializeMenuItem = menu.AddItem("Initialize", (Uri)null, colorX.Azure);
                initializeMenuItem.Button.LocalPressed += (_, _) =>
                {
                    var file = File.ReadAllText(findPath);
                    var deserialize = JsonSerializer.Deserialize<SerializedScript>(file);

                    var dynvarSpace = slot.GetComponentOrAttach<DynamicVariableSpace>();
                    dynvarSpace.SpaceName.Value = "Solder";
                    
                    foreach (var names in deserialize.ImportNames)
                    {
                        var type = names.Type.GetType(ResoniteScriptDeserializer.AllTypes);
                        var valueType = !type.GetInterfaces().Contains(typeof(IWorldElement));
                        if (valueType)
                            foreach (var sanitized in names.Names.Select(DynamicVariableHelper.SanitizeName))
                                DynamicVariableHelper.EnsureDynamicValueVariable(slot, sanitized, $"Solder/{sanitized}", type);
                        else
                            foreach (var sanitized in names.Names.Select(DynamicVariableHelper.SanitizeName))
                                DynamicVariableHelper.EnsureDynamicReferenceVariable(slot, sanitized, $"Solder/{sanitized}", type);
                    }
                };
                
                break;
            }
            case CompileMode.RedprintPack:
            {
                break;
            }
            case CompileMode.Barebones:
            {
                var scriptName = tag.Substring(("Compile(".Length), (tag.Length - 1) - ("Compile(".Length));
                var parsedName = SolderClient.SanitizeString(scriptName);

                if (string.IsNullOrWhiteSpace(parsedName)) break;
                
                var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                
                if (!File.Exists(findPath)) break;
                    
                var compileMenuItem = menu.AddItem("Compile Script", (Uri)null, colorX.Lime);
                compileMenuItem.Button.LocalPressed += (_, _) => CompileMethod(findPath, mode, monopack, persist, slot, slot);

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
                
                break;
            }
        }

        return;

        void CompileMethod(string findPath, CompileMode compileMode, bool compileMonopack, bool compilePersistent, Slot compileSlot, Slot importRoot)
        {
            var file = File.ReadAllText(findPath);
            var deserialize = JsonSerializer.Deserialize<SerializedScript>(file);

            var importNames = deserialize.ImportNames.ToDictionary(i => i.Type.GetType(ResoniteScriptDeserializer.AllTypes), i => i.Names);
                    
            SolderClient.Msg($"Script file version {deserialize.Version}");
            SolderClient.Msg($"Node count: {deserialize.Nodes.Count}, Connection count: {deserialize.Connections.AllConnections.Count}");
            try
            {
                var settings = new DeserializeSettings
                {
                    Mode = compileMode,
                    Monopack = compileMonopack,
                    Persistent = compilePersistent,
                    ImportNames = importNames,
                    ImportRoot = importRoot,
                };
                ResoniteScriptDeserializer.DeserializeScript(compileSlot, deserialize, settings);
                SolderClient.Msg("Finished deserializing");
            }
            catch (Exception e)
            {
                SolderClient.Msg("Deserialization failed with the following error");
                SolderClient.Msg(e.ToString());
            }
        }
    }

    private static readonly MethodInfo BarebonesHandleEnsureValueImportMethod =
        typeof(ProtoFluxToolPatch).GetMethod(nameof(BarebonesHandleEnsureValueImport),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly MethodInfo BarebonesHandleEnsureReferenceImportMethod =
        typeof(ProtoFluxToolPatch).GetMethod(nameof(BarebonesHandleEnsureReferenceImport),
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
}
