namespace RayaTrainer.Core.Assets;

public sealed record AssetPackManifest(
    int SchemaVersion,
    string Id,
    string Provider,
    string Version,
    string Attribution,
    IReadOnlyList<AssetPackEntry> Assets);
