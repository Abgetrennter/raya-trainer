using Xunit;

namespace RayaTrainer.Tests;

/// <summary>
/// 地址校验工具的解析器单元测试。验证 markdown registry 和配置源解析的正确性，
/// 以及故意不一致时能被检出。
/// </summary>
public sealed class AddressLintParserTests
{
    [Fact]
    public void OffsetsJsonParser_ExtractsQuotedHexAddresses()
    {
        var json = """
        {
          "lua_engine": {
            "lua_call": "0x403E30",
            "lua_version": "4.0.1"
          },
          "player": {
            "theGameClient": "0xCE8C9C"
          }
        }
        """;

        var result = RayaTrainer.AddressLint.TestAccess.ParseOffsetsJson(json);

        Assert.Equal(0x403E30u, result["lua_call"]);
        Assert.Equal(0xCE8C9Cu, result["theGameClient"]);
        Assert.False(result.ContainsKey("lua_version"), "非 hex 字符串值不应被解析为地址");
        Assert.False(result.ContainsKey("lua_engine"), "section 名不应被解析为地址");
    }

    [Fact]
    public void GameOffsetsHeaderParser_ExtractsUint32Constants()
    {
        var header = """
        struct GameOffsets {
            uint32_t theGameClient = 0xCE8C9C;
            uint32_t lua_call       = 0x403E30;
            std::string luaVersion = "4.0.1";
        };
        """;

        var result = RayaTrainer.AddressLint.TestAccess.ParseGameOffsetsHeader(header);

        Assert.Equal(0xCE8C9Cu, result["theGameClient"]);
        Assert.Equal(0x403E30u, result["lua_call"]);
        Assert.False(result.ContainsKey("luaVersion"), "非 uint32_t 成员不应被解析");
    }

    [Fact]
    public void RegistryParser_HandlesFiveColumnAndFourColumnTables()
    {
        var registry = """
        # 地址注册表

        ### 引擎单例指针

        | 符号 | IDA VA | RVA (CE) | 用途 | 出现位置 |
        |---|---|---|---|---|
        | `TheGameClient` | `0xCE8C9C` | `+8E8C9C` | GameClient | `offsets.json`、`GameOffsets.h` |

        ### 结构体字段偏移

        | 符号 | 偏移 | 用途 | 出现位置 |
        |---|---|---|---|
        | `luaStateOffset` | `0x24` | lua_State 偏移 | `offsets.json`、`GameOffsets.h` |

        ### 动态分配区域

        | 符号 | 偏移 |
        |---|---|
        | `iEnable` | 动态 |
        """;

        var result = RayaTrainer.AddressLint.TestAccess.ParseRegistry(registry);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("TheGameClient", result.Entries[0].Symbol);
        Assert.Equal(0xCE8C9Cu, result.Entries[0].IdaVa);
        Assert.Equal("luaStateOffset", result.Entries[1].Symbol);
        // 动态分配段应被跳过
        Assert.DoesNotContain(result.Entries, e => e.Symbol == "iEnable");
    }

    [Fact]
    public void CheckSymbol_DetectsMismatch()
    {
        var registry = RayaTrainer.AddressLint.TestAccess.ParseRegistry("""
        ### 函数

        | 符号 | IDA VA | RVA | 用途 | 出现位置 |
        |---|---|---|---|---|
        | `lua_call` | `0x403E30` | `+3E30` | call | `offsets.json`、`GameOffsets.h` |
        """);

        var offsetsJson = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["lua_call"] = 0x403E30,
        };
        var gameOffsetsH = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["lua_call"] = 0xDEADBEEF, // 故意不一致
        };

        var violations = new List<RayaTrainer.AddressLint.Program.Violation>();
        foreach (var entry in registry.Entries)
        {
            RayaTrainer.AddressLint.TestAccess.CheckSymbol(
                entry, offsetsJson, gameOffsetsH, violations);
        }

        var mismatch = Assert.Single(violations);
        Assert.Contains("GameOffsets.h", mismatch.Message);
        Assert.Contains("0xDEADBEEF", mismatch.Message);
    }
}
