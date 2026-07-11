using System.Globalization;
using System.Text.RegularExpressions;

namespace RayaTrainer.ContractLint;

/// <summary>
/// Regex-based parsers for the 4 cross-language contracts.
/// All methods take a repoRoot path and resolve source files relative to it.
/// </summary>
internal static partial class ContractParsers
{
    // ---------------------------------------------------------------
    // Regex patterns
    // ---------------------------------------------------------------

    /// <summary>
    /// C# const field: public const uint Name = 0x...;
    /// </summary>
    [GeneratedRegex(@"public\s+const\s+(?:uint|ushort|ulong|int)\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)[uUlL]*\s*;",
        RegexOptions.Compiled)]
    private static partial Regex CsConstField();

    /// <summary>
    /// C++ inline constexpr: inline constexpr uint32_t kName = 0x...u;
    /// </summary>
    [GeneratedRegex(@"inline\s+constexpr\s+\w+(?:::\w+)?\s+(k\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)[uUlL]*\s*;",
        RegexOptions.Compiled)]
    private static partial Regex CppConstexpr();

    /// <summary>
    /// Enum member: Name = Value,
    /// </summary>
    [GeneratedRegex(@"(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,?\s*(?://.*|/\*.*\*/)?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex EnumMember();

    /// <summary>
    /// C# string array element: "Value",
    /// </summary>
    [GeneratedRegex(@"""([^""]+)""\s*,?",
        RegexOptions.Compiled)]
    private static partial Regex CsStringArrayElement();

    /// <summary>
    /// Detect start of a C# enum block for the given name.
    /// </summary>
    [GeneratedRegex(@"enum\s+(?:\w+\s+)?(NativeFeatureStateId|AgentCommand)\s*:\s*\w+\s*\{",
        RegexOptions.Compiled)]
    private static partial Regex CsEnumBlockStart();

    /// <summary>
    /// Detect start of a C++ enum block (enum class) for the given name.
    /// </summary>
    [GeneratedRegex(@"enum\s+class\s+(NativeFeatureStateId|AgentCommand|NativeCatalogEntry)\s*:\s*\w+(?:::\w+)?\s*\{",
        RegexOptions.Compiled)]
    private static partial Regex CppEnumBlockStart();

    // ---------------------------------------------------------------
    // Public check methods — each returns a list of violations
    // ---------------------------------------------------------------

    /// <summary>
    /// Check that C# AgentProtocol.Magic/Version and AgentBuildIdentity.Fingerprint
    /// match C++ kAgentMagic/kAgentProtocolVersion/kAgentBuildFingerprint.
    /// </summary>
    public static List<ContractViolation> CheckProtocolConstants(string repoRoot)
    {
        var violations = new List<ContractViolation>();

        var csProtocol = ReadFileSafe(repoRoot, "src/RayaTrainer.Core/Agent/AgentProtocol.cs");
        var csIdentity = ReadFileSafe(repoRoot, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs");
        var cppProtocol = ReadFileSafe(repoRoot, "src/RayaTrainer.Agent/AgentProtocol.h");

        if (csProtocol is null || csIdentity is null || cppProtocol is null)
        {
            violations.Add(new("ProtocolConstants", "ParseError", "One or more source files could not be read."));
            return violations;
        }

        // Parse C# values
        var csMagic = ParseConstValue(csProtocol, "Magic");
        var csVersion = ParseConstValue(csProtocol, "Version");
        var csFingerprint = ParseConstValue(csIdentity, "Fingerprint");

        // Parse C++ values
        var cppMagic = ParseCppConstexpr(cppProtocol, "kAgentMagic");
        var cppVersion = ParseCppConstexpr(cppProtocol, "kAgentProtocolVersion");
        var cppFingerprint = ParseCppConstexpr(cppProtocol, "kAgentBuildFingerprint");

        // Cross-reference
        CompareConstant(violations, "ProtocolConstants", "Magic", csMagic, cppMagic);
        CompareConstant(violations, "ProtocolConstants", "Version", csVersion, cppVersion);
        CompareConstant(violations, "ProtocolConstants", "Fingerprint", csFingerprint, cppFingerprint);

        return violations;
    }

    /// <summary>
    /// Check that C# AgentCommand enum matches C++ AgentCommand enum exactly
    /// (all names and values identical).
    /// </summary>
    public static List<ContractViolation> CheckAgentCommand(string repoRoot)
    {
        var violations = new List<ContractViolation>();

        var csSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Core/Agent/AgentCommand.cs");
        var cppSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Agent/AgentProtocol.h");

        if (csSrc is null || cppSrc is null)
        {
            violations.Add(new("AgentCommand", "ParseError", "One or more source files could not be read."));
            return violations;
        }

        var csMembers = ParseEnumBody(csSrc, "AgentCommand", isCpp: false);
        var cppMembers = ParseEnumBody(cppSrc, "AgentCommand", isCpp: true);

        CompareEnums(violations, "AgentCommand", csMembers, cppMembers);

        return violations;
    }

    /// <summary>
    /// Check that C# NativeAgentCatalog.EntryNames order and count match
    /// C++ NativeCatalogEntry enum values, and that ExpectedEntryCount matches.
    /// </summary>
    public static List<ContractViolation> CheckNativeCatalogEntry(string repoRoot)
    {
        var violations = new List<ContractViolation>();

        var csSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Core/Agent/NativeAgentCatalog.cs");
        var cppSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Agent/AgentProtocol.h");

        if (csSrc is null || cppSrc is null)
        {
            violations.Add(new("NativeCatalogEntry", "ParseError", "One or more source files could not be read."));
            return violations;
        }

        // Extract C# EntryNames in order
        var csEntryNames = ExtractStringArray(csSrc, "EntryNames");

        // Extract C# ExpectedEntryCount
        var csExpectedCount = ParseConstValue(csSrc, "ExpectedEntryCount");

        // Extract C++ NativeCatalogEntry enum members
        var cppMembers = ParseEnumBody(cppSrc, "NativeCatalogEntry", isCpp: true);

        // Separate EntryCount from real entries
        var cppEntryCount = 0L;
        var cppByName = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var kvp in cppMembers)
        {
            if (kvp.Key == "EntryCount")
            {
                cppEntryCount = kvp.Value;
                continue;
            }
            cppByName[kvp.Key] = kvp.Value;
        }

        // Validate C# entry list vs C++ enum members (value = index)
        if (csEntryNames.Count != cppByName.Count)
        {
            violations.Add(new("NativeCatalogEntry", "CountMismatch",
                $"C# EntryNames has {csEntryNames.Count} entries, C++ NativeCatalogEntry has {cppByName.Count} members."));
        }

        // Compare names by index (value = index for NativeCatalogEntry)
        for (var i = 0; i < Math.Max(csEntryNames.Count, cppByName.Count); i++)
        {
            if (i < csEntryNames.Count && i < cppByName.Count)
            {
                // Both sides have entry at index i — compare
                var csName = csEntryNames[i];
                var cppEntry = cppByName.FirstOrDefault(kvp => kvp.Value == i);

                if (cppEntry.Key != csName)
                {
                    violations.Add(new("NativeCatalogEntry", "Mismatch",
                        $"Index {i}: C# EntryNames has \"{csName}\", C++ has \"{cppEntry.Key}\"."));
                }
            }
            else if (i < csEntryNames.Count)
            {
                violations.Add(new("NativeCatalogEntry", "Mismatch",
                    $"C# EntryNames[{i}] = \"{csEntryNames[i]}\" has no C++ counterpart at value {i}."));
            }
            else
            {
                var extra = cppByName.FirstOrDefault(kvp => kvp.Value == i);
                if (extra.Key != null)
                {
                    violations.Add(new("NativeCatalogEntry", "Mismatch",
                        $"C++ NativeCatalogEntry has \"{extra.Key}\" at value {i} with no C# EntryNames counterpart."));
                }
            }
        }

        // Compare ExpectedEntryCount vs EntryCount
        if (csExpectedCount.HasValue && cppEntryCount != 0)
        {
            if (csExpectedCount.Value != (ulong)cppEntryCount)
            {
                violations.Add(new("NativeCatalogEntry", "Mismatch",
                    $"C# ExpectedEntryCount={csExpectedCount.Value}, C++ EntryCount={cppEntryCount}."));
            }

            if (csExpectedCount.Value != (ulong)csEntryNames.Count)
            {
                violations.Add(new("NativeCatalogEntry", "Mismatch",
                    $"C# ExpectedEntryCount={csExpectedCount.Value} but EntryNames has {csEntryNames.Count} entries."));
            }

            if (cppEntryCount != cppByName.Count)
            {
                violations.Add(new("NativeCatalogEntry", "Mismatch",
                    $"C++ EntryCount={cppEntryCount} but NativeCatalogEntry has {cppByName.Count} real members."));
            }
        }
        else
        {
            violations.Add(new("NativeCatalogEntry", "ParseError",
                "Could not parse ExpectedEntryCount or EntryCount."));
        }

        return violations;
    }

    /// <summary>
    /// Check that C# NativeFeatureStateId enum matches C++ NativeFeatureStateId enum
    /// exactly (all names and values identical).
    /// </summary>
    public static List<ContractViolation> CheckNativeFeatureStateId(string repoRoot)
    {
        var violations = new List<ContractViolation>();

        var csSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Core/Agent/NativeFeatureStateId.cs");
        var cppSrc = ReadFileSafe(repoRoot, "src/RayaTrainer.Agent/AgentFeatureState.h");

        if (csSrc is null || cppSrc is null)
        {
            violations.Add(new("NativeFeatureStateId", "ParseError", "One or more source files could not be read."));
            return violations;
        }

        var csMembers = ParseEnumBody(csSrc, "NativeFeatureStateId", isCpp: false);
        var cppMembers = ParseEnumBody(cppSrc, "NativeFeatureStateId", isCpp: true);

        CompareEnums(violations, "NativeFeatureStateId", csMembers, cppMembers);

        return violations;
    }

    // ---------------------------------------------------------------
    // Internal helpers (exposed via TestAccess)
    // ---------------------------------------------------------------

    internal static ulong? ParseConstValue(string source, string name)
    {
        var match = CsConstField().Match(source);
        while (match.Success)
        {
            if (match.Groups[1].Value == name)
            {
                return ParseNumber(match.Groups[2].Value);
            }
            match = match.NextMatch();
        }

        return null;
    }

    internal static ulong? ParseCppConstexpr(string source, string name)
    {
        var match = CppConstexpr().Match(source);
        while (match.Success)
        {
            if (match.Groups[1].Value == name)
            {
                return ParseNumber(match.Groups[2].Value);
            }
            match = match.NextMatch();
        }

        return null;
    }

    internal static Dictionary<string, long> ParseEnumBody(string source, string enumName, bool isCpp)
    {
        var result = new Dictionary<string, long>();

        // Extract the enum block body (content between { and })
        var body = ExtractEnumBlock(source, enumName, isCpp);
        if (string.IsNullOrEmpty(body))
            return result;

        // Parse each member line
        var lines = body.ReplaceLineEndings().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Skip single-line comments
            if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                continue;

            var match = EnumMember().Match(trimmed);
            if (match.Success)
            {
                var memberName = match.Groups[1].Value;
                var valueStr = match.Groups[2].Value;
                var value = valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt64(valueStr, 16)
                    : long.Parse(valueStr, CultureInfo.InvariantCulture);
                result[memberName] = value;
            }
        }

        return result;
    }

    internal static List<string> ExtractStringArray(string source, string arrayName)
    {
        var result = new List<string>();

        // Find the array initialization: Name = [ ... ]
        var arrayPattern = new Regex(
            Regex.Escape(arrayName) + @"\s*=\s*\[(.*?)\]",
            RegexOptions.Singleline | RegexOptions.Compiled);

        var match = arrayPattern.Match(source);
        if (!match.Success)
            return result;

        var arrayBody = match.Groups[1].Value;

        // Extract string literals
        foreach (Match elemMatch in CsStringArrayElement().Matches(arrayBody))
        {
            result.Add(elemMatch.Groups[1].Value);
        }

        return result;
    }

    internal static ulong? ParseNumber(string raw)
    {
        var sanitized = StripSuffix(raw);
        if (sanitized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(sanitized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var val))
                return val;
            return null;
        }

        if (ulong.TryParse(sanitized, NumberStyles.None, CultureInfo.InvariantCulture, out var dec))
            return dec;

        return null;
    }

    internal static string StripSuffix(string raw)
    {
        var result = raw.Trim();
        // Remove trailing u/U/l/L/ul/ull/UL/ULL suffixes
        while (result.Length > 0 && (result[^1] == 'u' || result[^1] == 'U' || result[^1] == 'l' || result[^1] == 'L'))
        {
            result = result[..^1];
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private static string? ReadFileSafe(string repoRoot, string relativePath)
    {
        var fullPath = Path.Combine(repoRoot, relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    /// <summary>
    /// Extract the body content between { and } of an enum declaration.
    /// Works for both C# and C++ enum syntax.
    /// Uses regex with Singleline to capture across lines.
    /// </summary>
    private static string ExtractEnumBlock(string source, string enumName, bool isCpp)
    {
        // Build a pattern to match the enum header and capture the body
        // C#: public enum Name : type { body }
        // C++: enum class Name : type { body };
        var escapedName = Regex.Escape(enumName);
        string pattern;

        if (isCpp)
        {
            // C++: enum class Foo : uint32_t { ... };
            pattern = @"enum\s+class\s+" + escapedName + @"(?:\s*:\s*\w+(?:::\w+)?)?\s*\{(.*?)\}\s*;";
        }
        else
        {
            // C#: public enum Foo : uint { ... }
            pattern = @"enum\s+\w*\s*" + escapedName + @"\s*:\s*\w+\s*\{(.*?)\}";
        }

        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static void CompareConstant(
        List<ContractViolation> violations,
        string contract,
        string name,
        ulong? csValue,
        ulong? cppValue)
    {
        if (csValue is null)
        {
            violations.Add(new(contract, "ParseError", $"C# {name} could not be parsed."));
            return;
        }

        if (cppValue is null)
        {
            violations.Add(new(contract, "ParseError", $"C++ {name} could not be parsed."));
            return;
        }

        if (csValue.Value != cppValue.Value)
        {
            violations.Add(new(contract, "Mismatch",
                $"{name}: C# = 0x{csValue.Value:X}, C++ = 0x{cppValue.Value:X}."));
        }
    }

    private static void CompareEnums(
        List<ContractViolation> violations,
        string contract,
        Dictionary<string, long> csMembers,
        Dictionary<string, long> cppMembers)
    {
        // Check count
        if (csMembers.Count != cppMembers.Count)
        {
            violations.Add(new(contract, "CountMismatch",
                $"C# has {csMembers.Count} members, C++ has {cppMembers.Count}."));
        }

        // Check C# members exist in C++
        foreach (var csKvp in csMembers)
        {
            if (!cppMembers.TryGetValue(csKvp.Key, out var cppVal))
            {
                violations.Add(new(contract, "Mismatch",
                    $"C# member \"{csKvp.Key}\" (value={csKvp.Value}) not found in C++."));
                continue;
            }

            if (csKvp.Value != cppVal)
            {
                violations.Add(new(contract, "Mismatch",
                    $"Member \"{csKvp.Key}\": C# value={csKvp.Value}, C++ value={cppVal}."));
            }
        }

        // Check C++ members not in C#
        foreach (var cppKvp in cppMembers)
        {
            if (!csMembers.ContainsKey(cppKvp.Key))
            {
                violations.Add(new(contract, "Mismatch",
                    $"C++ member \"{cppKvp.Key}\" (value={cppKvp.Value}) not found in C#."));
            }
        }
    }
}
