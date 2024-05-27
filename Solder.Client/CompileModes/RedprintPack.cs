using FrooxEngine;

namespace Solder.Client.CompileModes;

public class RedprintPack : BaseCompileMode
{
    public override CompileMode Mode => CompileMode.RedprintPack;
    public override void GenerateMenu(Slot slot, ContextMenu menu, bool monopack, bool persist)
    {
        //not implemented yet
    }
    public override T Import<T>(int index) => this.DynamicImport<T>(index);
    public override Sync<T> ImportValue<T>(int index) => this.DynamicImportValue<T>(index);
    public override SyncRef<T> ImportReference<T>(int index) => this.DynamicImportReference<T>(index);
}
