using RayaTrainer.Core.Agent;

namespace RayaTrainer.ApiGenerator;

public static class DirectGameApiGenerationVerifier
{
    public static IReadOnlyList<string> VerifyRepository(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        }

        var root = Path.GetFullPath(repositoryRoot);
        var catalogPath = Path.Combine(root, "src", "RayaTrainer.Core", "Agent", "apis.json");
        var generatedRoot = Path.Combine(root, "src");

        using var stream = File.OpenRead(catalogPath);
        var catalog = DirectGameApiCatalog.Load(stream);

        var diagnostics = new List<string>();
        diagnostics.AddRange(VerifyGeneratedSources(catalog, generatedRoot));
        return diagnostics;
    }

    public static IReadOnlyList<string> VerifyGeneratedSources(
        DirectGameApiCatalog catalog,
        string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        }

        var diagnostics = new List<string>();
        IReadOnlyList<GeneratedDirectGameApiFile> expectedFiles;
        try
        {
            expectedFiles = DirectGameApiShadowGenerator.Generate(catalog);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(ex.Message);
            return diagnostics;
        }

        foreach (var expected in expectedFiles)
        {
            var actualPath = Path.Combine(
                outputRoot,
                expected.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(actualPath))
            {
                diagnostics.Add($"Missing generated source {actualPath}.");
                continue;
            }

            var actual = File.ReadAllText(actualPath);
            if (!NormalizeNewlines(actual).Equals(NormalizeNewlines(expected.Content), StringComparison.Ordinal))
            {
                diagnostics.Add($"Generated source drift: {actualPath}.");
            }
        }

        return diagnostics;
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
