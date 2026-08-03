namespace RayaTrainer.App.Web.State;

public sealed record TrainerWebStateMessage(
    string Type,
    string Message,
    bool? Success = null,
    TrainerWebStatusResponse? SessionStatus = null,
    TrainerGameStateResponse? GameState = null,
    TrainerSelectedUnitResponse? SelectedUnit = null,
    TrainerFeaturesResponse? Features = null)
{
    public static TrainerWebStateMessage Status(string message, TrainerWebStatusResponse? status = null)
    {
        return new TrainerWebStateMessage("status", message, SessionStatus: status);
    }

    public static TrainerWebStateMessage Command(TrainerWebCommandResult result)
    {
        return new TrainerWebStateMessage("command", result.Message, result.Success);
    }

    public static TrainerWebStateMessage Heartbeat()
    {
        return new TrainerWebStateMessage("heartbeat", "ok");
    }

    public static TrainerWebStateMessage GameStateUpdate(TrainerGameStateResponse gameState)
    {
        return new TrainerWebStateMessage("game-state", "游戏状态已更新", GameState: gameState);
    }

    public static TrainerWebStateMessage SelectedUnitUpdate(TrainerSelectedUnitResponse selectedUnit)
    {
        return new TrainerWebStateMessage("selected-unit", "选中单位已更新", SelectedUnit: selectedUnit);
    }

    public static TrainerWebStateMessage FeaturesUpdate(TrainerFeaturesResponse features)
    {
        return new TrainerWebStateMessage("features", "功能状态已更新", Features: features);
    }
}
