namespace RayaTrainer.ContractLint;

/// <summary>
/// Test access shim: exposes internal parser methods to the test project
/// via InternalsVisibleTo, without changing the production API surface.
/// Mirrors the pattern from tools/RayaTrainer.AddressLint/TestAccess.cs.
/// </summary>
internal static class TestAccess
{
    public static ulong? ParseConstValue(string source, string name) =>
        ContractParsers.ParseConstValue(source, name);

    public static ulong? ParseCppConstexpr(string source, string name) =>
        ContractParsers.ParseCppConstexpr(source, name);

    public static Dictionary<string, long> ParseEnumBody(string source, string enumName, bool isCpp) =>
        ContractParsers.ParseEnumBody(source, enumName, isCpp);

    public static List<string> ExtractStringArray(string source, string arrayName) =>
        ContractParsers.ExtractStringArray(source, arrayName);

    public static ulong? ParseNumber(string raw) =>
        ContractParsers.ParseNumber(raw);

    public static string StripSuffix(string raw) =>
        ContractParsers.StripSuffix(raw);
}
