namespace RayaTrainer.Core.Errors;

/// <summary>
/// Domain → HTTP status mapping for external error responses (design doc §4.5). Kept in
/// Core as plain integers so Core stays free of any ASP.NET dependency.
/// </summary>
public static class TrainerErrorHttp
{
    public const int ServiceUnavailable = 503;
    public const int Conflict = 409;
    public const int BadRequest = 400;
    public const int InternalServerError = 500;

    public static int ToStatusCode(ErrorDomain domain) => domain switch
    {
        ErrorDomain.Env => ServiceUnavailable,
        ErrorDomain.Contract => Conflict,
        ErrorDomain.Request => BadRequest,
        ErrorDomain.Execution => InternalServerError,
        ErrorDomain.Fault => InternalServerError,
        _ => InternalServerError,
    };
}
