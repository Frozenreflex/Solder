using FrooxEngine;

namespace Solder.Client.CompileModes;

public static class CompileModeHelper
{
    public static T DynamicImport<T>(this BaseCompileMode mode, int index)
    {
        var space = mode.Settings.ImportRoot.GetComponent<DynamicVariableSpace>();
        if (space is null) return default;
        var nameList = mode.Settings.ImportNames[typeof(T)];
        if (nameList is null) return default;
        if (index >= nameList.Count) return default;
        var name = nameList[index];
        var sanitized = DynamicVariableHelper.SanitizeName(name);
        return space.TryReadValue<T>(sanitized, out var value) ? value : default;
    }
    public static Sync<T> DynamicImportValue<T>(this BaseCompileMode mode, int index)
    {
        var nameList = mode.Settings.ImportNames[typeof(T)];
        if (nameList is null) return default;
        if (index >= nameList.Count) return default;
        var name = nameList[index];
        var sanitized = DynamicVariableHelper.SanitizeName(name);
        var component = mode.Settings.ImportRoot.GetComponent<DynamicValueVariable<T>>(i => i.VariableName.Value.EndsWith(sanitized));
        return component?.Value;
    }
    public static SyncRef<T> DynamicImportReference<T>(this BaseCompileMode mode, int index) where T : class, IWorldElement
    {
        var nameList = mode.Settings.ImportNames[typeof(T)];
        if (nameList is null) return default;
        if (index >= nameList.Count) return default;
        var name = nameList[index];
        var sanitized = DynamicVariableHelper.SanitizeName(name);
        var component = mode.Settings.ImportRoot.GetComponent<DynamicReferenceVariable<T>>(i => i.VariableName.Value.EndsWith(sanitized));
        return component?.Reference;
    }
}
