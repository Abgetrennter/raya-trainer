namespace RayaTrainer.ApiGenerator;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is >= 1 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            return Verify(args);
        }

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: RayaTrainer.ApiGenerator <apis.json> <output-root>");
            Console.Error.WriteLine("       RayaTrainer.ApiGenerator verify [repository-root]");
            return 1;
        }

        try
        {
            using var stream = File.OpenRead(args[0]);
            var catalog = Core.Agent.DirectGameApiCatalog.Load(stream);
            var files = DirectGameApiShadowGenerator.WriteToDirectory(catalog, args[1]);
            Console.WriteLine($"Generated {files.Count} Direct GameApi shadow files under {args[1]}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int Verify(string[] args)
    {
        if (args.Length > 2)
        {
            Console.Error.WriteLine("Usage: RayaTrainer.ApiGenerator verify [repository-root]");
            return 1;
        }

        try
        {
            var repositoryRoot = args.Length == 2
                ? args[1]
                : FindRepositoryRoot(Environment.CurrentDirectory);
            var diagnostics = DirectGameApiGenerationVerifier.VerifyRepository(repositoryRoot);
            if (diagnostics.Count == 0)
            {
                Console.WriteLine("Direct GameApi generated sources and native routing contract are in sync.");
                return 0;
            }

            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
