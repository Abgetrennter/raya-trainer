namespace RayaTrainer.Core.Versions;

public sealed class UnsupportedSymbolException : InvalidOperationException
{
    public UnsupportedSymbolException(string profileId, string catalogName, string symbolicName, AddressSupportStatus status)
        : base($"Profile '{profileId}' does not support {catalogName}.{symbolicName} ({status}).")
    {
        ProfileId = profileId;
        CatalogName = catalogName;
        SymbolicName = symbolicName;
        Status = status;
    }

    public string ProfileId { get; }

    public string CatalogName { get; }

    public string SymbolicName { get; }

    public AddressSupportStatus Status { get; }
}
