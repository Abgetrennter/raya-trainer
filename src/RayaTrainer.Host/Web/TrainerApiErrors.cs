using Microsoft.AspNetCore.Http;
using RayaTrainer.Core.Errors;

namespace RayaTrainer.Host.Web;

/// <summary>
/// Unified Web/WS error response helpers (design doc §4.5, roadmap M2.5). Every endpoint
/// failure is produced from a <see cref="TrainerErrorMapping"/> so the response carries the
/// vocabulary code and the domain-mapped HTTP status. <see cref="Body"/> emits the §4.5
/// unified shape for surfaces without legacy client parsing; <see cref="CommandResult"/>
/// keeps the <see cref="TrainerWebCommandResult"/> body the built-in remote page parses
/// (reasonCode + message fallback) while still applying the unified code and status.
/// </summary>
public static class TrainerApiErrors
{
    public sealed record ErrorBody(string Code, string Message, string RetryHint);

    public static IResult Body(TrainerErrorMapping mapping, string message) =>
        Results.Json(
            new { error = new ErrorBody(TrainerErrorVocabulary.ToEventCode(mapping.Code), message, mapping.RetryHint.ToString()) },
            statusCode: TrainerErrorHttp.ToStatusCode(mapping.Domain));

    public static IResult CommandResult(TrainerErrorMapping mapping, string message) =>
        Results.Json(
            new TrainerWebCommandResult(false, message, mapping.Code),
            statusCode: TrainerErrorHttp.ToStatusCode(mapping.Domain));
}
