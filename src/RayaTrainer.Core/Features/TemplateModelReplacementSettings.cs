namespace RayaTrainer.Core.Features;

public sealed record TemplateModelReplacementSettings
{
    public static TemplateModelReplacementSettings Parse(string targetUnitIdText, string donorUnitIdText)
    {
        return new TemplateModelReplacementSettings(
            UnitCodeParser.Parse(targetUnitIdText),
            UnitCodeParser.Parse(donorUnitIdText));
    }

    public TemplateModelReplacementSettings(uint targetUnitId, uint donorUnitId)
    {
        if (targetUnitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUnitId), "Target unit id must be non-zero.");
        }

        if (donorUnitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(donorUnitId), "Donor unit id must be non-zero.");
        }

        TargetUnitId = targetUnitId;
        DonorUnitId = donorUnitId;
    }

    public uint TargetUnitId { get; }

    public uint DonorUnitId { get; }
}
