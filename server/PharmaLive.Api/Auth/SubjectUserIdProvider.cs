using Microsoft.AspNetCore.SignalR;

namespace PharmaLive.Api.Auth;

/// <summary>
/// MapInboundClaims is disabled, so the default provider (ClaimTypes.NameIdentifier) would
/// return null and Clients.User(...) would silently match nobody. The "sub" claim is the user id.
/// </summary>
public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value;
}
