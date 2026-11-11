using System;
using System.IO;
using System.Reflection;

namespace RayaTrainer.Core.RuntimeAssets;

// Releases the embedded 4-stream AttributeModifier bundle to a stable absolute directory and returns
// the manifest absolute path for AgentCommand.LoadBundledRuntimeAssets (plan Phase 1, decision ②:
// embed in Core -> release at runtime -> host hands absolute path to the Agent). The four streams
// must land in the same directory under their canonical names so the engine manifest loader resolves
// mod.bin/mod.relo/mod.imp relative to the manifest. This is private (excluded from the public
// projection) because it carries the bundled research assets.
//
// Two format variants are embedded (experiment-lab integration, four-profile support):
//   * Game 6 (RA3 1.12 / 1.13): the original SDK-X BinaryAssetBuilder output.
//   * Game 7 (Uprising 1.0 / 1.1): the same data body with Uprising's extended stream headers
//     (8-byte manifest magic 00 00 00 00 07 00 00 01; 4-byte bin/imp/relo magics BB BA / B1 BA /
//     BE BA). Produced by SDK-U SageManifestHeaderFixer + SageBinImpReloHeaderFixer — the data
//     body is byte-identical, only the file-head magic differs.
// The host selects the variant based on the current target's engine family.
public static class RuntimeAssetPathProvider
{
    private const string StreamPrefixGame6 = "RayaTrainer.Core.RuntimeAssets.AttributeModifiers.mod.";
    private const string StreamPrefixGame7 = "RayaTrainer.Core.RuntimeAssets.AttributeModifiers.mod.uprising.";
    private static readonly string[] StreamNames = { "manifest", "bin", "relo", "imp" };

    // Absolute directory the bundle is released into. Lives next to the trainer settings/exe so the
    // Agent (in the game process) can read it without elevation.
    public static string BundleDirectory =>
        Path.Combine(AppContext.BaseDirectory, "runtime-assets", "attribute-modifiers");

    // Releases the four streams (idempotent: overwrites if the content differs so a bundle update is
    // picked up on the next trainer launch). Returns the absolute manifest path. Throws on failure.
    // Set uprising=true to release the Game 7 variant for Uprising targets.
    public static string EnsureBundleReleased(bool uprising = false)
    {
        Directory.CreateDirectory(BundleDirectory);
        var assembly = typeof(RuntimeAssetPathProvider).Assembly;
        var prefix = uprising ? StreamPrefixGame7 : StreamPrefixGame6;
        foreach (var name in StreamNames)
        {
            var logical = prefix + name;
            using var stream = assembly.GetManifestResourceStream(logical)
                ?? throw new FileNotFoundException($"Embedded bundle stream '{logical}' not found.");
            var target = Path.Combine(BundleDirectory, "mod." + name);
            using var fs = File.Create(target);
            stream.CopyTo(fs);
        }
        return Path.Combine(BundleDirectory, "mod.manifest");
    }
}
