using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Elements.Core;
using FrooxEngine;

namespace Solder.Client.CompileModes;

public class Solder : BaseCompileMode
{
    public override CompileMode Mode => CompileMode.Solder;
    public override void GenerateMenu(Slot slot, ContextMenu menu, bool monopack, bool persist)
    {
        var parsedName = SolderClient.SanitizeString(slot.Name);

        if (string.IsNullOrWhiteSpace(parsedName)) return;
                
        var findPath = Path.Combine(SolderClient.ScriptPath, $"{parsedName}.pfscript");
                
        if (!File.Exists(findPath)) return;
                
        var compileMenuItem = menu.AddItem("Compile Script", (Uri)null, colorX.Lime);
        compileMenuItem.Button.LocalPressed += (_, _) => CompileButtonMethod(findPath, this, monopack, persist, slot, slot);
                
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
    }
    public override T Import<T>(int index) => this.DynamicImport<T>(index);
    public override Sync<T> ImportValue<T>(int index) => this.DynamicImportValue<T>(index);
    public override SyncRef<T> ImportReference<T>(int index) => this.DynamicImportReference<T>(index);
}
