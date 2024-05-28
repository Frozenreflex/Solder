using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FrooxEngine;

namespace Solder.Client;

public abstract class BaseCompileMode
{
    public DeserializeSettings Settings;
    public virtual bool SupportsMonopack => false;
    public abstract CompileMode Mode { get; }
    public abstract void GenerateMenu(Slot slot, ContextMenu menu, bool monopack, bool persist);
    public abstract T Import<T>(int index);
    public abstract Sync<T> ImportValue<T>(int index);
    public abstract SyncRef<T> ImportReference<T>(int index) where T : class, IWorldElement;
    public abstract void CleanupPreviousCompile(Slot nodeRoot);

    public static implicit operator CompileMode(BaseCompileMode mode) => mode.Mode;
    
    protected static void CompileButtonMethod(string findPath, BaseCompileMode compileMode, bool compileMonopack, bool compilePersistent, Slot compileSlot, Slot importRoot, Slot nodeRoot)
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
            compileMode.Settings = settings;
            ResoniteScriptDeserializer.DeserializeScript(nodeRoot, deserialize, settings);
            SolderClient.Msg("Finished deserializing");
        }
        catch (Exception e)
        {
            SolderClient.Msg("Deserialization failed with the following error");
            SolderClient.Msg(e.ToString());
        }
    }
}
