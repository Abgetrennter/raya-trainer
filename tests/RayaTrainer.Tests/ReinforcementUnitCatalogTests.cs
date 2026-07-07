using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReinforcementUnitCatalogTests
{
    [Fact]
    public void ParseReadsCodesAndNamesFromCodeListLines()
    {
        var units = ReinforcementUnitCatalog.Parse(new[]
        {
            "原版 RA3,盟军,DDFC28DE,警犬",
            "",
            "起义时刻,升阳,0x6586A5A0,奥米茄百合子",
            "原版 RA3\t苏联\td741d327\t恐怖机器人"
        });

        Assert.Collection(
            units,
            unit =>
            {
                Assert.Equal("原版 RA3", unit.Mod);
                Assert.Equal("盟军", unit.Faction);
                Assert.Equal(0xDDFC28DEu, unit.Code);
                Assert.Equal("警犬", unit.Name);
                Assert.Equal("0xDDFC28DE", unit.CodeText);
            },
            unit =>
            {
                Assert.Equal("起义时刻", unit.Mod);
                Assert.Equal("升阳", unit.Faction);
                Assert.Equal(0x6586A5A0u, unit.Code);
                Assert.Equal("奥米茄百合子", unit.Name);
                Assert.Equal("0x6586A5A0", unit.CodeText);
            },
            unit =>
            {
                Assert.Equal("原版 RA3", unit.Mod);
                Assert.Equal("苏联", unit.Faction);
                Assert.Equal(0xD741D327u, unit.Code);
                Assert.Equal("恐怖机器人", unit.Name);
                Assert.Equal("0xD741D327", unit.CodeText);
            });
    }

    [Fact]
    public void ParseReadsOptionalSourceIdFromFiveColumnLines()
    {
        var units = ReinforcementUnitCatalog.Parse(new[]
        {
            "日冕\t神州\t0x19CE7A0E\t守护者坦克\tCUAntiVehicleVehicleTech0",
            "日冕,苏联,0xD40F74F6,基地车,SovietMCV_Enhanced"
        });

        Assert.Collection(
            units,
            unit =>
            {
                Assert.Equal("日冕", unit.Mod);
                Assert.Equal("神州", unit.Faction);
                Assert.Equal(0x19CE7A0Eu, unit.Code);
                Assert.Equal("守护者坦克", unit.Name);
                Assert.Equal("CUAntiVehicleVehicleTech0", unit.SourceId);
            },
            unit =>
            {
                Assert.Equal("日冕", unit.Mod);
                Assert.Equal("苏联", unit.Faction);
                Assert.Equal(0xD40F74F6u, unit.Code);
                Assert.Equal("基地车", unit.Name);
                Assert.Equal("SovietMCV_Enhanced", unit.SourceId);
            });
    }

    [Fact]
    public void ParseKeepsSourceIdEmptyForLegacyFourColumnLines()
    {
        var unit = Assert.Single(ReinforcementUnitCatalog.Parse(new[]
        {
            "原版 RA3,盟军,DDFC28DE,警犬"
        }));

        Assert.Null(unit.SourceId);
    }

    [Theory]
    [InlineData("EA8F676B")]
    [InlineData("ea8f676b")]
    [InlineData("0xEA8F676B")]
    [InlineData("0Xea8f676b")]
    public void ParseAcceptsHexCodesWithOptionalPrefixAndAnyCase(string codeText)
    {
        var units = ReinforcementUnitCatalog.Parse(new[]
        {
            $"起义时刻,盟军,{codeText},冷冻兵团"
        });

        Assert.Single(units);
        Assert.Equal(0xEA8F676Bu, units[0].Code);
        Assert.Equal("0xEA8F676B", units[0].CodeText);
    }

    [Fact]
    public void ParseSkipsMalformedLines()
    {
        var units = ReinforcementUnitCatalog.Parse(new[]
        {
            "not-a-code",
            "12345",
            "# comment",
            "  ",
            "原版 RA3,苏联,AF4C0DA5,MCV"
        });

        Assert.Single(units);
        Assert.Equal(0xAF4C0DA5u, units[0].Code);
        Assert.Equal("MCV", units[0].Name);
    }

    [Fact]
    public void LoadBuiltInContainsRepresentativeEntries()
    {
        var units = ReinforcementUnitCatalog.LoadBuiltIn();

        Assert.NotEmpty(units);
        Assert.Contains(units, unit => unit.Mod == "起义时刻" && unit.Faction == "盟军" && unit.Code == 0xEA8F676B && unit.Name == "冷冻兵团");
        Assert.Contains(units, unit => unit.Mod == "原版 RA3" && unit.Faction == "升阳" && unit.Code == 0x6586A5A0 && unit.Name == "奥米茄百合子");
        Assert.Contains(units, unit => unit.Mod == "原版 RA3" && unit.Faction == "盟军" && unit.Code == 0x28DA574E && unit.Name == "MCV");
    }

    [Fact]
    public void CoronaImportListContainsFilteredUnitsWithWebIds()
    {
        var units = ReinforcementUnitCatalog.Load(CoronaImportListPath());

        Assert.Equal(436, units.Count);
        Assert.All(units, unit =>
        {
            Assert.Equal("日冕", unit.Mod);
            Assert.False(string.IsNullOrWhiteSpace(unit.SourceId));
            Assert.NotEqual("-", unit.Name);
        });
        Assert.Contains(units, unit =>
            unit.Faction == "神州" &&
            unit.Code == 0x19CE7A0E &&
            unit.Name == "守护者坦克" &&
            unit.SourceId == "CUAntiVehicleVehicleTech0");
    }

    [Fact]
    public void MergeKeepsBuiltInEntriesAndAppendsOnlyNewCodesPerMod()
    {
        var merged = ReinforcementUnitCatalog.Merge(
            new[]
            {
                new ReinforcementUnitEntry("原版 RA3", "盟军", 0x11111111, "A"),
                new ReinforcementUnitEntry("原版 RA3", "盟军", 0x22222222, "B")
            },
            new[]
            {
                new ReinforcementUnitEntry("原版 RA3", "苏联", 0x22222222, "B-override"),
                new ReinforcementUnitEntry("起义时刻", "盟军", 0x22222222, "B uprising"),
                new ReinforcementUnitEntry("原版 RA3", "盟军", 0x33333333, "C")
            });

        Assert.Collection(
            merged,
            unit => Assert.Equal(("原版 RA3", 0x11111111u, "A"), (unit.Mod, unit.Code, unit.Name)),
            unit => Assert.Equal(("原版 RA3", 0x22222222u, "B"), (unit.Mod, unit.Code, unit.Name)),
            unit => Assert.Equal(("起义时刻", 0x22222222u, "B uprising"), (unit.Mod, unit.Code, unit.Name)),
            unit => Assert.Equal(("原版 RA3", 0x33333333u, "C"), (unit.Mod, unit.Code, unit.Name)));
    }

    [Fact]
    public void LoadWithExternalFileMergesBuiltInAndCustomEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ReinforcementUnitCatalog.CustomFileName);
        File.WriteAllLines(path, new[]
        {
            "自定义模组,天启军团,0x11111111,自定义坦克",
            "自定义模组,天启军团,11111111,重复自定义坦克"
        });

        var units = ReinforcementUnitCatalog.LoadWithCustomFile(directory);

        Assert.Contains(units, unit => unit.Mod == "自定义模组" && unit.Faction == "天启军团" && unit.Code == 0x11111111u && unit.Name == "自定义坦克");
        Assert.Equal(1, units.Count(unit => unit.Mod == "自定义模组" && unit.Code == 0x11111111u));
    }

    [Fact]
    public void ImportAppendsOnlyNewValidEntriesToCustomFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var existing = new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DEu, "警犬")
        };

        var result = ReinforcementUnitCatalog.ImportToCustomFile(
            directory,
            new[]
            {
                "原版 RA3,盟军,DDFC28DE,警犬重复",
                "起义时刻,盟军,0Xea8f676b,冷冻兵团",
                "bad-line"
            },
            existing);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(1, result.InvalidCount);

        var path = Path.Combine(directory, ReinforcementUnitCatalog.CustomFileName);
        Assert.Equal("起义时刻,盟军,0xEA8F676B,冷冻兵团", Assert.Single(File.ReadAllLines(path)));
    }

    [Fact]
    public void ImportPersistsSourceIdForFiveColumnEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = ReinforcementUnitCatalog.ImportToCustomFile(
            directory,
            new[]
            {
                "日冕\t神州\t0x19CE7A0E\t守护者坦克\tCUAntiVehicleVehicleTech0"
            },
            Array.Empty<ReinforcementUnitEntry>());

        Assert.Equal(1, result.AddedCount);
        var path = Path.Combine(directory, ReinforcementUnitCatalog.CustomFileName);
        Assert.Equal("日冕,神州,0x19CE7A0E,守护者坦克,CUAntiVehicleVehicleTech0", Assert.Single(File.ReadAllLines(path)));
    }

    private static string CoronaImportListPath()
    {
        return Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.Core",
            "Assets",
            "Catalogs",
            "Corona",
            "reinforcements.txt");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
