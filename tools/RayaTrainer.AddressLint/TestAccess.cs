using System.Collections.Generic;

namespace RayaTrainer.AddressLint;

/// <summary>
/// 测试门面：暴露 internal parser 给测试项目，不改变生产 API 表面。
/// </summary>
internal static class TestAccess
{
    public static Program.AddressRegistry ParseRegistry(string content) => AddressRegistryParser.Parse(content);

    public static Dictionary<string, uint> ParseOffsetsJson(string content) => OffsetsJsonParser.Parse(content);

    public static Dictionary<string, uint> ParseGameOffsetsHeader(string content) => GameOffsetsHeaderParser.Parse(content);

    public static void CheckSymbol(
        Program.RegistryEntry entry,
        Dictionary<string, uint> offsetsJson,
        Dictionary<string, uint> gameOffsetsH,
        List<Program.Violation> violations) => Program.CheckSymbol(entry, offsetsJson, gameOffsetsH, violations);
}
