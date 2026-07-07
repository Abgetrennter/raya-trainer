using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RayaTrainer.AddressLint;

/// <summary>
/// 地址一致性校验工具。
/// 读取 address_registry.md 作为真相源，扫描 offsets.json / GameOffsets.h，
/// 报告同一符号在不同源中的地址不一致或缺失。不自动改写。
/// </summary>
public static class Program
{
    private const int OkExitCode = 0;
    private const int ViolationExitCode = 1;
    private const int ErrorExitCode = 2;

    public static int Main(string[] args)
    {
        var repoRoot = ResolveRepoRoot();
        var registryPath = Path.Combine(repoRoot, "RA3_Analysis", "07_修改点索引", "address_registry.md");
        var offsetsJsonPath = Path.Combine(repoRoot, "tools", "Ra3LuaConsole", "injector", "offsets.json");
        var gameOffsetsHPath = Path.Combine(repoRoot, "tools", "Ra3LuaConsole", "dll", "config", "GameOffsets.h");

        var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        try
        {
            var registry = AddressRegistryParser.Parse(File.ReadAllText(registryPath));
            var offsetsJson = OffsetsJsonParser.Parse(File.ReadAllText(offsetsJsonPath));
            var gameOffsetsH = GameOffsetsHeaderParser.Parse(File.ReadAllText(gameOffsetsHPath));

            var violations = new List<Violation>();

            // 1. 同一符号跨源地址不一致
            foreach (var entry in registry.Entries)
            {
                CheckSymbol(entry, offsetsJson, gameOffsetsH, violations);
            }

            // 2. offsets.json/GameOffsets.h 中存在但 registry 未登记的地址
            ReportUnregistered(offsetsJson, registry, "offsets.json", violations);
            ReportUnregistered(gameOffsetsH, registry, "GameOffsets.h", violations);

            if (violations.Count == 0)
            {
                Console.WriteLine($"地址一致性校验通过：{registry.Entries.Count} 个登记地址，跨源一致。");
                if (verbose)
                {
                    Console.WriteLine($"  offsets.json: {offsetsJson.Count} 项");
                    Console.WriteLine($"  GameOffsets.h: {gameOffsetsH.Count} 项");
                }

                return OkExitCode;
            }

            Console.WriteLine($"地址一致性校验发现 {violations.Count} 个问题：");
            Console.WriteLine();
            foreach (var violation in violations)
            {
                Console.WriteLine($"  [{violation.Severity}] {violation.Symbol}");
                Console.WriteLine($"    {violation.Message}");
                Console.WriteLine();
            }

            return ViolationExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"地址校验工具异常：{ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex);
            }

            return ErrorExitCode;
        }
    }

    internal static void CheckSymbol(
        RegistryEntry entry,
        Dictionary<string, uint> offsetsJson,
        Dictionary<string, uint> gameOffsetsH,
        List<Violation> violations)
    {
        // 只对 IDA VA 形式的登记项做跨源检查（跳过 trainer 内部偏移和 CE 表达式）
        if (entry.IdaVa is not { } expectedVa)
        {
            return;
        }

        foreach (var sourceName in entry.SourceSymbols)
        {
            switch (sourceName.Source)
            {
                case AddressSource.OffsetsJson:
                    if (offsetsJson.TryGetValue(sourceName.Symbol, out var jsonAddr) && jsonAddr != expectedVa)
                    {
                        violations.Add(new Violation(
                            entry.Symbol,
                            Severity.Mismatch,
                            $"offsets.json[{sourceName.Symbol}]=0x{jsonAddr:X} 与 registry 0x{expectedVa:X} 不一致。"));
                    }

                    break;
                case AddressSource.GameOffsetsH:
                    if (gameOffsetsH.TryGetValue(sourceName.Symbol, out var headerAddr) && headerAddr != expectedVa)
                    {
                        violations.Add(new Violation(
                            entry.Symbol,
                            Severity.Mismatch,
                            $"GameOffsets.h[{sourceName.Symbol}]=0x{headerAddr:X} 与 registry 0x{expectedVa:X} 不一致。"));
                    }

                    break;
            }
        }
    }

    private static void ReportUnregistered(
        Dictionary<string, uint> source,
        AddressRegistry registry,
        string sourceLabel,
        List<Violation> violations)
    {
        // registry 主符号名（第一列）在各源中作为 key 使用，也收集 SourceSymbols 里的显式符号名
        var registeredSymbols = registry.Entries
            .Select(e => e.Symbol)
            .Concat(registry.Entries
                .SelectMany(e => e.SourceSymbols)
                .Where(s => LabelForSource(s.Source) == sourceLabel)
                .Select(s => s.Symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in source)
        {
            if (!registeredSymbols.Contains(kvp.Key))
            {
                violations.Add(new Violation(
                    kvp.Key,
                    Severity.Unregistered,
                    $"{sourceLabel} 含地址 0x{kvp.Value:X}（符号 {kvp.Key}）但 address_registry.md 未登记。"));
            }
        }
    }

    private static string LabelForSource(AddressSource source) => source switch
    {
        AddressSource.OffsetsJson => "offsets.json",
        AddressSource.GameOffsetsH => "GameOffsets.h",
        _ => source.ToString(),
    };

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    public sealed record RegistryEntry(
        string Symbol,
        uint? IdaVa,
        IReadOnlyList<SourceSymbol> SourceSymbols);

    public sealed record SourceSymbol(AddressSource Source, string Symbol);

    public enum AddressSource
    {
        OffsetsJson,
        GameOffsetsH,
    }

    public sealed record Violation(string Symbol, Severity Severity, string Message);

    public sealed record AddressRegistry(IReadOnlyList<RegistryEntry> Entries);

    public enum Severity
    {
        Mismatch,
        Unregistered,
    }
}
internal static class AddressRegistryParser
{
    public static Program.AddressRegistry Parse(string content)
    {
        var entries = new List<Program.RegistryEntry>();
        var lines = content.ReplaceLineEndings().Split('\n');
        var currentSection = string.Empty;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSection = trimmed[4..].Trim();
                continue;
            }

            // 跳过动态分配区域段（trainer 内部布局，无固定游戏地址）
            if (currentSection.Contains("动态分配", StringComparison.Ordinal))
            {
                continue;
            }

            // markdown 表行：| 符号 | VA | RVA | 用途 | 出现位置 |（5 列）
            // 或 | 符号 | 偏移 | 用途 | 出现位置 |（4 列，结构体字段段）
            if (!trimmed.StartsWith('|') || trimmed.StartsWith("|---") || trimmed.StartsWith("| 符号"))
            {
                continue;
            }

            var cells = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();
            if (cells.Length < 4)
            {
                continue;
            }

            var symbol = cells[0].Replace("`", "").Trim();
            var idaVaText = cells[1].Replace("`", "").Trim();
            uint? idaVa = null;
            if (idaVaText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(idaVaText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var va))
                {
                    idaVa = va;
                }
            }

            // 5 列表的出现位置在 cells[4]；4 列表（结构体字段段）在 cells[3]
            var sourcesCell = cells.Length >= 5 ? cells[4] : cells[3];

            var sourceSymbols = ParseSourceSymbols(sourcesCell, symbol);
            entries.Add(new Program.RegistryEntry(symbol, idaVa, sourceSymbols));
        }

        return new Program.AddressRegistry(entries);
    }

    private static List<Program.SourceSymbol> ParseSourceSymbols(string sourcesCell, string primarySymbol)
    {
        var result = new List<Program.SourceSymbol>();
        var cleaned = sourcesCell.Replace("`", "");
        var tokens = cleaned.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var symbolName = ExtractSymbolName(token);
            // 当出现位置列没带括号符号名时，用 registry 主符号名兜底
            if (string.IsNullOrEmpty(symbolName) || symbolName == token.Trim())
            {
                symbolName = primarySymbol;
            }

            if (token.Contains("offsets.json", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new Program.SourceSymbol(Program.AddressSource.OffsetsJson, symbolName));
            }
            else if (token.Contains("GameOffsets.h", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new Program.SourceSymbol(Program.AddressSource.GameOffsetsH, symbolName));
            }
        }

        return result;
    }

    private static string ExtractSymbolName(string token)
    {
        var paren = token.IndexOf('(');
        if (paren >= 0)
        {
            var close = token.IndexOf(')', paren);
            if (close > paren)
            {
                return token[(paren + 1)..close].Trim();
            }
        }

        // token 形如 "GameOffsets.h"，无显式符号名，用首字段回退
        return token;
    }
}

internal static class OffsetsJsonParser
{
    public static Dictionary<string, uint> Parse(string content)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        // 只匹配 "key": "0x..." 紧邻键值对，避免把 section 名或非地址值误当地址
        var pattern = new Regex(@"""([a-zA-Z_][a-zA-Z0-9_]*)""\s*:\s*""(0x[0-9A-Fa-f]+)""", RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(content))
        {
            if (uint.TryParse(match.Groups[2].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                result[match.Groups[1].Value] = value;
            }
        }

        return result;
    }
}
internal static class GameOffsetsHeaderParser
{
    public static Dictionary<string, uint> Parse(string content)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var pattern = new Regex(@"uint32_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+)", RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(content))
        {
            if (uint.TryParse(match.Groups[2].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                result[match.Groups[1].Value] = value;
            }
        }

        return result;
    }
}
