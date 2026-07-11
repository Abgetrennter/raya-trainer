using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RayaTrainer.App.Web.Auth;
using RayaTrainer.App.Web.WebSockets;

namespace RayaTrainer.App.Web;

public static class TrainerApiEndpoints
{
    public static IEndpointRouteBuilder MapTrainerApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/pair", async (
            TrainerPairingRequest request,
            HttpContext context,
            IDeviceApprovalService approvalService,
            DevicePairingTokenStore tokenStore,
            CancellationToken cancellationToken) =>
        {
            var approved = await approvalService.ApproveAsync(
                    CreateApprovalRequest(request, context),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!approved)
            {
                return Results.Json(
                    new TrainerPairingResponse(false, null, "设备未允许。"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var token = tokenStore.IssueToken();
            return Results.Ok(new TrainerPairingResponse(true, token, "设备已配对。"));
        });

        api.MapGet("/status", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetStatus());
        });

        api.MapGet("/diagnostics", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetDiagnostics());
        });

        api.MapGet("/features", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetFeatures());
        });

        api.MapGet("/presets", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetPresets());
        });

        api.MapGet("/reinforcements/catalog", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetReinforcementCatalog());
        });

        api.MapGet("/secret-protocols/catalog", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            return unauthorized ?? Results.Ok(handler.GetSecretProtocolCatalog());
        });

        api.MapGet("/ws", TrainerWebSocketEndpoint.HandleAsync);

        api.MapPost("/toggles/{featureId}", async (
            string featureId,
            TrainerToggleStateRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.SetToggleAsync(
                    new TrainerToggleRequest(featureId, request.Enabled),
                    cancellationToken)
                .ConfigureAwait(false));
        });

        api.MapPost("/resources", async (
            TrainerResourceRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.WriteResourcesAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/reinforcements/execute", async (
            TrainerReinforcementRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.ExecuteReinforcementAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/reinforcements/queue/execute", async (
            TrainerReinforcementQueueRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.ExecuteReinforcementQueueAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/secret-protocols/grant", async (
            TrainerSecretProtocolRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.GrantSecretProtocolAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/secret-protocols/queue/grant", async (
            TrainerSecretProtocolQueueRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.GrantSecretProtocolQueueAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/actions/{featureId}", async (
            string featureId,
            TrainerActionRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.ExecuteActionAsync(
                    featureId, request, cancellationToken)
                .ConfigureAwait(false));
        });

        api.MapGet("/selected-unit", (
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            var result = handler.ReadSelectedUnit();
            return result is not null
                ? Results.Ok(result)
                : Results.BadRequest(new TrainerWebCommandResult(false, "无法读取选中单位信息。"));
        });

        api.MapPost("/template/model", async (
            TrainerTemplateModelReplacementRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.ReplaceTemplateModelAsync(request, cancellationToken).ConfigureAwait(false));
        });

        api.MapPost("/template/weapon", async (
            TrainerTemplateWeaponReplacementRequest request,
            HttpContext context,
            DevicePairingTokenStore tokenStore,
            TrainerApiHandler handler,
            CancellationToken cancellationToken) =>
        {
            var unauthorized = RequireAuthorized(context, tokenStore);
            if (unauthorized is not null)
            {
                return unauthorized;
            }

            return ToHttpResult(await handler.ReplaceTemplateWeaponAsync(request, cancellationToken).ConfigureAwait(false));
        });

        return endpoints;
    }

    private static DeviceApprovalRequest CreateApprovalRequest(
        TrainerPairingRequest request,
        HttpContext context)
    {
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName)
            ? "未知设备"
            : request.DeviceName.Trim();
        return new DeviceApprovalRequest(
            deviceName,
            context.Request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static IResult? RequireAuthorized(
        HttpContext context,
        DevicePairingTokenStore tokenStore)
    {
        return tokenStore.ValidateBearer(context.Request.Headers.Authorization.ToString())
            ? null
            : Results.Unauthorized();
    }

    private static IResult ToHttpResult(TrainerWebCommandResult result)
    {
        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }

    private static IResult ToHttpResult(TrainerWebQueueResult result)
    {
        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }
}
