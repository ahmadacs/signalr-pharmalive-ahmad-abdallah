using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace PharmaLive.Api.Hubs.Filters;

/// <summary>
/// SignalR's equivalent of middleware: wraps every hub invocation. Logs hub, method, user and
/// duration, and translates unexpected exceptions into a safe, readable HubException.
/// </summary>
public sealed class HubLoggingFilter(ILogger<HubLoggingFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var stopwatch = Stopwatch.StartNew();
        var hub = context.Hub.GetType().Name;
        var method = context.HubMethodName;
        var user = context.Context.UserIdentifier ?? "anonymous";

        try
        {
            var result = await next(context);

            logger.LogInformation(
                "{Hub}.{Method} by {User} completed in {ElapsedMs} ms.",
                hub, method, user, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (HubException)
        {
            // Already client-friendly (validation / domain errors).
            logger.LogWarning(
                "{Hub}.{Method} by {User} was rejected after {ElapsedMs} ms.",
                hub, method, user, stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (OperationCanceledException) when (context.Context.ConnectionAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "{Hub}.{Method} by {User} was cancelled after {ElapsedMs} ms (client left the page).",
                hub, method, user, stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "{Hub}.{Method} by {User} failed after {ElapsedMs} ms.",
                hub, method, user, stopwatch.ElapsedMilliseconds);

            throw new HubException($"'{method}' failed. Please try again.");
        }
    }
}
