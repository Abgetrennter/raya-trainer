using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Memory;

public sealed class AddressResolver
{
    private readonly nint _moduleBase;
    private readonly IReadOnlyDictionary<string, nint> _symbols;
    private readonly Ra3VersionProfile? _profile;

    public AddressResolver(nint moduleBase, IReadOnlyDictionary<string, nint> symbols, Ra3VersionProfile? profile = null)
    {
        _moduleBase = moduleBase;
        _symbols = new Dictionary<string, nint>(symbols, StringComparer.OrdinalIgnoreCase);
        _profile = profile;
    }

    public nint Resolve(string expression)
    {
        var parts = expression.Split('+', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (_symbols.TryGetValue(parts[0], out var symbolAddress))
            {
                return symbolAddress;
            }
            throw new InvalidOperationException($"Unknown address expression '{expression}'.");
        }

        var offset = Convert.ToInt32(parts[1], 16);
        var matchesProfileProcess = _profile is not null
            && parts[0].Equals(_profile.ProcessName, StringComparison.OrdinalIgnoreCase);
        var matchesLegacyProcess = parts[0].Equals(GameTarget.ProcessName, StringComparison.OrdinalIgnoreCase)
            && (_profile is null || _profile.ProcessName.Equals(GameTarget.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (matchesLegacyProcess || matchesProfileProcess)
        {
            return _moduleBase + offset;
        }

        if (_symbols.TryGetValue(parts[0], out var baseAddress))
        {
            return baseAddress + offset;
        }

        throw new InvalidOperationException($"Unknown symbol '{parts[0]}'.");
    }
}
