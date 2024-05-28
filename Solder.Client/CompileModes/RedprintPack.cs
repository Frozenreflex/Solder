using System;
using System.Linq;
using System.Text.RegularExpressions;
using FrooxEngine;
using Elements.Assets;

namespace Solder.Client.CompileModes;

public class RedprintPack : BaseCompileMode
{
    public static string SanitizeRedprintName(string name)
    {
        //TODO: actually strip rich text tags properly instead of bodging it
        //<color=#80FFE7>TextDrive</color> - 6 Nodes

        if (string.IsNullOrWhiteSpace(name)) return name;

        var stripRtf = new StringRenderTree(name).GetRawString();

        //matches the whitespace before and after the dash, the dash itself, the number, the whitespace between the number and the word Nodes, and Nodes
        var headerMatch = Regex.Match(stripRtf, @"\s-\s[0-9]+\sNodes");
        
        if (!headerMatch.Success) return name;
        
        return stripRtf.Substring(0, headerMatch.Index);
    }
    public override CompileMode Mode => CompileMode.RedprintPack;
    public override void GenerateMenu(Slot slot, ContextMenu menu, bool monopack, bool persist)
    {
        var name = slot.Name;
        var parsedName = SolderClient.SanitizeString(SanitizeRedprintName(name));
    }
    public override T Import<T>(int index) => this.DynamicImport<T>(index);
    public override Sync<T> ImportValue<T>(int index) => this.DynamicImportValue<T>(index);
    public override SyncRef<T> ImportReference<T>(int index) => this.DynamicImportReference<T>(index);
    public override void CleanupPreviousCompile(Slot nodeRoot)
    {
        var children = nodeRoot.Children.ToList().Where(c => c.Tag == "Compiled").ToList();
        foreach (var c in children) c.Destroy();
    }
}
