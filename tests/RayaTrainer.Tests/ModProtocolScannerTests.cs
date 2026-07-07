using System.Text;
using RayaTrainer.Core.Features;
using RayaTrainer.ModProtocolScanner;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ModProtocolScannerTests
{
    [Fact]
    public void BigArchiveReaderReadsManifestEntryFromBig4File()
    {
        var directory = CreateTempDirectory();
        var archivePath = Path.Combine(directory, "Sample.big");
        WriteBig4Archive(archivePath, new Dictionary<string, string>
        {
            ["data/mod.manifest"] = "PlayerTech:PlayerTech_Allied_T4Tower"
        });

        var text = BigArchiveReader.ReadText(archivePath, "data/mod.manifest");

        Assert.Equal("PlayerTech:PlayerTech_Allied_T4Tower", text);
    }

    [Fact]
    public void SkudefParserResolvesAddBigPathsRelativeToSkudefDirectory()
    {
        var directory = CreateTempDirectory();
        var dataDirectory = Path.Combine(directory, "Data");
        Directory.CreateDirectory(dataDirectory);
        var bigPath = Path.Combine(dataDirectory, "Sample.big");
        File.WriteAllText(bigPath, string.Empty);
        var skudefPath = Path.Combine(directory, "Sample.skudef");
        File.WriteAllText(skudefPath, """
            mod-game 1.12
            add-big Data\Sample.big
            add-big "Missing Folder\Other.big"
            """);

        var paths = SkudefParser.GetBigPaths(skudefPath);

        Assert.Equal(
            [
                Path.GetFullPath(bigPath),
                Path.GetFullPath(Path.Combine(directory, "Missing Folder", "Other.big"))
            ],
            paths);
    }

    [Fact]
    public void ManifestAssetExtractorCollectsProtocolRelatedAssets()
    {
        const string manifest = """
            PlayerTech:PlayerTech_Allied_T4Tower
            PurchasePlayerTechButtonTemplate:Purchase_PlayerTech_Allied_T4Tower
            PlayerPowerButtonTemplate:Command_SpecialPowerAlliedT4Tower
            SpecialPowerTemplate:SpecialPowerAlliedT4Tower
            UpgradeTemplate:Upgrade_AlliedT4Tower
            """;

        var assets = ManifestAssetExtractor.Extract(manifest);

        Assert.Contains("PlayerTech_Allied_T4Tower", assets.PlayerTechs);
        Assert.Contains("Purchase_PlayerTech_Allied_T4Tower", assets.PurchaseButtons);
        Assert.Contains("Command_SpecialPowerAlliedT4Tower", assets.PowerButtons);
        Assert.Contains("SpecialPowerAlliedT4Tower", assets.SpecialPowers);
        Assert.Contains("Upgrade_AlliedT4Tower", assets.Upgrades);
    }

    [Fact]
    public void ClassifierKeepsProtocolCandidatesAndExcludesKnownNonProtocols()
    {
        var assets = new ManifestAssets(
            PlayerTechs:
            [
                "PlayerTech_Allied_T4Tower",
                "PlayerTech_Allied_HighTechnology",
                "PlayerTech_Allied_BlackEagleUpgrade",
                "PlayerTech_Generic_Rank_1",
                "PlayerTech_AI_Boost",
                "PlayerTech_Allied",
                "PlayerTech_HideProtocols"
            ],
            PurchaseButtons:
            [
                "Purchase_PlayerTech_Allied_BlackEagleUpgrade"
            ],
            PowerButtons:
            [
                "Command_SpecialPowerAlliedBlackEagle"
            ],
            SpecialPowers:
            [
                "SpecialPowerAlliedBlackEagle",
                "SpecialPowerHighTechnology"
            ],
            Upgrades:
            [
                "Upgrade_AlliedBlackEagle",
                "Upgrade_AlliedHighTechnology",
                "Upgrade_AlliedT4Tower"
            ]);

        var rows = SecretProtocolCandidateClassifier.Classify("Sample Mod", assets);

        Assert.Contains(rows, row => row.PlayerTech == "PlayerTech_Allied_T4Tower" && row.Upgrade == "Upgrade_AlliedT4Tower");
        Assert.Contains(rows, row => row.PlayerTech == "PlayerTech_Allied_HighTechnology" && row.Name == "高科技");
        Assert.Contains(rows, row => row.PlayerTech == "PlayerTech_Allied_BlackEagleUpgrade" && row.SpecialPower == "SpecialPowerAlliedBlackEagle");
        Assert.DoesNotContain(rows, row => row.PlayerTech == "PlayerTech_Generic_Rank_1");
        Assert.DoesNotContain(rows, row => row.PlayerTech == "PlayerTech_AI_Boost");
        Assert.DoesNotContain(rows, row => row.PlayerTech == "PlayerTech_Allied");
        Assert.DoesNotContain(rows, row => row.PlayerTech == "PlayerTech_HideProtocols");
    }

    [Fact]
    public void ImportWriterEmitsSixColumnRowsParseableBySecretProtocolCatalog()
    {
        var rows = new[]
        {
            new SecretProtocolCandidate("Sample Mod", "盟军", "T4 Tower", "PlayerTech_Allied_T4Tower", "Upgrade_AlliedT4Tower", "SpecialPowerAlliedT4Tower")
        };

        var lines = SecretProtocolImportWriter.Format(rows);
        var parsed = SecretProtocolCatalog.Parse(lines);

        var row = Assert.Single(parsed);
        Assert.Equal("Sample Mod", row.Mod);
        Assert.Equal("盟军", row.Faction);
        Assert.Equal("T4 Tower", row.Name);
        Assert.Equal("PlayerTech_Allied_T4Tower", row.PlayerTech);
        Assert.Equal("Upgrade_AlliedT4Tower", row.Upgrade);
        Assert.Equal("SpecialPowerAlliedT4Tower", row.SpecialPower);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteBig4Archive(string path, IReadOnlyDictionary<string, string> files)
    {
        var entries = files
            .Select(item => new
            {
                item.Key,
                PathBytes = Encoding.ASCII.GetBytes(item.Key),
                Data = Encoding.UTF8.GetBytes(item.Value)
            })
            .ToArray();
        var headerSize = 16 + entries.Sum(entry => 8 + entry.PathBytes.Length + 1);
        var offset = headerSize;
        var table = entries
            .Select(entry =>
            {
                var current = new ArchiveFixtureEntry(entry.Key, entry.PathBytes, entry.Data, offset);
                offset += entry.Data.Length;
                return current;
            })
            .ToArray();

        using var stream = File.Create(path);
        WriteAscii(stream, "BIG4");
        WriteUInt32LittleEndian(stream, (uint)offset);
        WriteUInt32BigEndian(stream, (uint)table.Length);
        WriteUInt32BigEndian(stream, (uint)headerSize);

        foreach (var entry in table)
        {
            WriteUInt32BigEndian(stream, (uint)entry.Offset);
            WriteUInt32BigEndian(stream, (uint)entry.Data.Length);
            stream.Write(entry.PathBytes);
            stream.WriteByte(0);
        }

        foreach (var entry in table)
        {
            stream.Write(entry.Data);
        }
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32LittleEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private sealed record ArchiveFixtureEntry(string Key, byte[] PathBytes, byte[] Data, int Offset);
}
