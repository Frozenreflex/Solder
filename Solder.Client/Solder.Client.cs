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

public class SolderClient : ResoniteMod
{
    public override string Name => "Solder.Client";
    public override string Author => "Fro Zen";
    public override string Version => "1.0.0";
    
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> Monopack =
        new("monopack", "Monopack", () => false);
    
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> Persistence =
        new("persistence", "Persistence", () => true);

    internal static ModConfiguration Config;

    public override void OnEngineInit()
    {
        var harmony = new Harmony("Solder.Client");
        harmony.PatchAll();
        Config = GetConfiguration();
    }
}

[HarmonyPatch(typeof(ProtoFluxTool))]
public static class ProtoFluxToolPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ProtoFluxTool.GenerateMenuItems))]
    public static void GenerateMenuItems(InteractionHandler tool, ContextMenu menu, ProtoFluxTool __instance)
    {
        var grabbed = __instance.GetGrabbedReference();
        
        //TODO: make this work for compiling multiple scripts in a hierarchy, and also allow overriding the config using tags

        if (grabbed is not Slot slot) return;
        
        var tag = slot.Tag;
        
        if (!tag.StartsWith("Compile(") || !tag.EndsWith(")")) return;
        
        var scriptName = tag.Substring(("Compile(".Length), (tag.Length - 1) - ("Compile(".Length));
        var parsedName = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(scriptName));

        if (string.IsNullOrWhiteSpace(parsedName)) return;
        
        var compileMenuItem = menu.AddItem("Compile Script", (Uri)null, colorX.Lime);
        compileMenuItem.Button.LocalPressed += (_, _) =>
        {
            //TODO: config
            var findPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", $"{parsedName}.pfscript");
            if (!File.Exists(findPath)) return;
            var file = File.ReadAllText(findPath);
            var deserialize = JsonSerializer.Deserialize<SerializedScript>(file);
            SolderClient.Msg($"Script file version {deserialize.Version}");
            SolderClient.Msg($"Node count: {deserialize.Nodes.Count}, Connection count: {deserialize.Connections.AllConnections.Count}");
            try
            {
                var monopack = SolderClient.Config.GetValue(SolderClient.Monopack);
                var persist = SolderClient.Config.GetValue(SolderClient.Persistence);
                ResoniteScriptDeserializer.DeserializeScript(slot, deserialize, new DeserializeSettings { Monopack = monopack, Persistent = persist });
                SolderClient.Msg("Finished deserializing");
            }
            catch (Exception e)
            {
                SolderClient.Msg("Deserialization failed with the following error");
                SolderClient.Msg(e.ToString());
            }
        };

        var ensureImportsMenuItem = menu.AddItem("Ensure Imports", (Uri)null, colorX.Azure);
        ensureImportsMenuItem.Button.LocalPressed += (_, _) =>
        {
            //TODO: config
            var findPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", $"{parsedName}.pfscript");
            if (!File.Exists(findPath)) return;
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
                        HandleEnsureValueImportMethod.MakeGenericMethod(type).Invoke(null, [slot, count]);
                    else
                        HandleEnsureReferenceImportMethod.MakeGenericMethod(type).Invoke(null, [slot, count]);
                }
                catch
                {
                    // ignored
                }
            }
        };
    }

    private static readonly MethodInfo HandleEnsureValueImportMethod =
        typeof(ProtoFluxToolPatch).GetMethod(nameof(HandleEnsureValueImport),
            BindingFlags.Static | BindingFlags.NonPublic);
    
    private static readonly MethodInfo HandleEnsureReferenceImportMethod =
        typeof(ProtoFluxToolPatch).GetMethod(nameof(HandleEnsureReferenceImport),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static void HandleEnsureValueImport<T>(Slot root, int count)
    {
        var multiplexer = root.GetComponentOrAttach<ValueMultiplexer<T>>();
        if (multiplexer.Values.Count >= count) return;
        while (multiplexer.Values.Count < count) multiplexer.Values.Add();
    }

    private static void HandleEnsureReferenceImport<T>(Slot root, int count) where T : class, IWorldElement
    {
        var multiplexer = root.GetComponentOrAttach<ReferenceMultiplexer<T>>();
        if (multiplexer.References.Count >= count) return;
        while (multiplexer.References.Count < count) multiplexer.References.Add();
    }
}
