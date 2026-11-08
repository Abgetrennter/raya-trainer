using RayaTrainer.Core.Errors;

namespace RayaTrainer.Core.Versions;

public sealed class UnsupportedSymbolException : TrainerException
{
    public const string Code = "CONTRACT.UNSUPPORTED_SYMBOL";

    public UnsupportedSymbolException(string profileId, string catalogName, string symbolicName, AddressSupportStatus status)
        : base(
            ErrorDomain.Contract,
            Code,
            RetryHint.NotRetryable,
            $"Profile '{profileId}' does not support {catalogName}.{symbolicName} ({status}).")
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
