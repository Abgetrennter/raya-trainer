namespace RayaTrainer.Core.Assets;

public sealed class AssetPackException : Exception
{
    public AssetPackException(string message) : base(message) { }
    public AssetPackException(string message, Exception inner) : base(message, inner) { }
}
