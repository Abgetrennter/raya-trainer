namespace RayaTrainer.Core.Features;

/// <summary>命名预设的最小公共身份；具体内容和装载语义仍由各预设类型负责。</summary>
public interface INamedPreset
{
    string Name { get; }
}

/// <summary>
/// 命名预设列表的公共名称策略：OrdinalIgnoreCase 查找、原位覆盖和按名称删除。
/// 不负责捕获、装载、执行、持久化或 Overlay 投影。
/// </summary>
public static class NamedPresetList
{
    public static int FindIndex<TPreset>(IList<TPreset> presets, string name)
        where TPreset : INamedPreset
    {
        for (var index = 0; index < presets.Count; index++)
        {
            if (presets[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }

    public static bool ContainsName<TPreset>(IList<TPreset> presets, string name, int exceptIndex = -1)
        where TPreset : INamedPreset
    {
        for (var index = 0; index < presets.Count; index++)
        {
            if (index != exceptIndex &&
                presets[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static int Upsert<TPreset>(IList<TPreset> presets, TPreset preset)
        where TPreset : INamedPreset
    {
        var index = FindIndex(presets, preset.Name);
        if (index >= 0)
        {
            presets[index] = preset;
            return index;
        }

        presets.Add(preset);
        return presets.Count - 1;
    }

    public static bool RemoveByName<TPreset>(IList<TPreset> presets, string name)
        where TPreset : INamedPreset
    {
        var index = FindIndex(presets, name);
        if (index < 0)
        {
            return false;
        }

        presets.RemoveAt(index);
        return true;
    }
}
