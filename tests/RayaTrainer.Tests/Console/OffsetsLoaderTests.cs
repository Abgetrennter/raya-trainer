namespace RayaTrainer.Tests.Console;

using Ra3LuaConsole.Config;
using Xunit;

public class OffsetsLoaderTests
{
    private const string SampleJson = """
    {
      "lua_engine": {
        "theLuaScriptEngine": "0xCDD17C",
        "luaStateOffset": "0x24",
        "lua_call": "0x403E30",
        "luaL_loadbuffer": "0x404050",
        "luaL_loadfile": "0x403EE0",
        "lua_gettop": "0x40A3D0",
        "lua_settop": "0x40A3E0",
        "lua_pushvalue": "0x40A4D0",
        "lua_pushnumber": "0x40A910",
        "lua_pushstring": "0x40A980",
        "lua_pushcclosure": "0x40A9E0",
        "lua_pushnil": "0x40A8F0",
        "lua_type": "0x40A520",
        "lua_tostring": "0x40A790",
        "lua_tonumber": "0x40A700",
        "lua_toboolean": "0x40A750",
        "lua_gettable": "0x4071B0",
        "lua_settable": "0x407250",
        "lua_setglobal": "0x40ACA0",
        "lua_getglobal": "0x40AA50",
        "lua_settagmethod": "0x40A2B0",
        "luaL_openlibs": "0xA74190",
        "lua_version": "4.0.1"
      },
      "player": {
        "theGameClient": "0xCE8C9C",
        "theGameLogic": "0xCD8CE4",
        "getCurrentPlayer": "0x4393E0",
        "gameSelection": "0xCDB73C",
        "game_update": "0x626620"
      },
      "upgrade": {
        "theUpgradeCenter": "0xCDBBF0",
        "findByName": "0x1456F0",
        "findTemplateByName": "0x147260",
        "grant": "0x44D7C0",
        "remove": "0x44A4E0",
        "hasUpgrade": "0x44A2D0",
        "getUpgradeContainer": "0x6DD460"
      },
      "pause": {
        "pauseManagerDoPause": "0x4E03F0",
        "pauseManagerUnpause": "0x4E0390",
        "gameSpeedApply": "0x6BE860"
      }
    }
    """;

    [Fact]
    public void Load_FromValidJson_ReturnsConfig()
    {
        var config = OffsetsLoader.Load(SampleJson);

        Assert.NotNull(config);
        Assert.Equal("0xCDD17C", config.LuaEngine.TheLuaScriptEngine);
        Assert.Equal("0x404050", config.LuaEngine.LuaLLoadbuffer);
        Assert.Equal("0x403E30", config.LuaEngine.LuaCall);
        Assert.Equal("0x40A790", config.LuaEngine.LuaTostring);
        Assert.Equal("0x40A700", config.LuaEngine.LuaTonumber);
        Assert.Equal("0x40A750", config.LuaEngine.LuaToboolean);
        Assert.Equal("4.0.1", config.LuaEngine.LuaVersion);
        Assert.Equal("0xCE8C9C", config.Player.TheGameClient);
        Assert.Equal("0xCDB73C", config.Player.GameSelection);
        Assert.Equal("0x626620", config.Player.GameUpdate);
        Assert.Equal("0x4E03F0", config.Pause!.PauseManagerDoPause);
    }

    [Fact]
    public void ResolveHex_ParsesHexAddressString()
    {
        Assert.Equal((uint)0xCDD17C, OffsetsLoader.ResolveHex("0xCDD17C"));
        Assert.Equal((uint)0x404050, OffsetsLoader.ResolveHex("0x404050"));
    }

    [Fact]
    public void ResolveHex_WhenEmpty_ReturnsZero()
    {
        Assert.Equal((uint)0, OffsetsLoader.ResolveHex(""));
        Assert.Equal((uint)0, OffsetsLoader.ResolveHex(null));
    }

    [Fact]
    public void Load_FromMalformedJson_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => OffsetsLoader.Load("{ not json"));
    }

    [Fact]
    public void LoadFromFile_ReadsFileCorrectly()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleJson);
            var config = OffsetsLoader.LoadFromFile(tempPath);
            Assert.Equal("0xCDD17C", config.LuaEngine.TheLuaScriptEngine);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
