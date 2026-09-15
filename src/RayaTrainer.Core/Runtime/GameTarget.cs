namespace RayaTrainer.Core.Runtime;

/// <summary>
/// Centralized constants for the target game version and process.
/// </summary>
public static class GameTarget
{
    /// <summary>
    /// Expected file version of the RA3 game executable.
    /// Used for version validation when attaching to the game process.
    /// </summary>
    public const string ExpectedVersion = "1.12.3444.25830";

    /// <summary>
    /// Process name of the RA3 game (without .exe extension).
    /// The launcher starts RA3.exe which spawns this process.
    /// </summary>
    public const string ProcessName = "ra3_1.12.game";
}
