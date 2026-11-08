using RayaTrainer.Core.Errors;

namespace RayaTrainer.Core.Assets;

public sealed class AssetPackException : TrainerException
{
    public const string Code = "CONTRACT.ASSET_PACK_INVALID";

    public AssetPackException(string message)
        : base(ErrorDomain.Contract, Code, RetryHint.NotRetryable, message) { }

    public AssetPackException(string message, Exception inner)
        : base(ErrorDomain.Contract, Code, RetryHint.NotRetryable, message, inner) { }
}
