using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PharmaLive.Api.Auth;
using PharmaLive.Api.Contracts;
using PharmaLive.Api.Services;

namespace PharmaLive.Api.Hubs;

/// <summary>
/// Hubs are TRANSIENT: a new instance per invocation, and invocations may run concurrently.
/// All state therefore lives in singleton services (PrescriptionStore / PresenceTracker).
/// </summary>
[Authorize]
public sealed class PharmacyHub(
    PrescriptionStore store,
    BranchCatalog branches,
    PresenceTracker presence,
    ILogger<PharmacyHub> logger) : Hub<IPharmacyClient>
{
    private string UserName => Context.UserIdentifier
        ?? throw new HubException("Unauthenticated connection.");

    private string Role => Context.User?.FindFirst("role")?.Value ?? string.Empty;

    private bool IsPharmacist => Role.Equals(TokenService.PharmacistRole, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- lifetime

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation(
            "Connection {ConnectionId} opened for {User} ({Role}).",
            Context.ConnectionId, UserName, Role);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR already removed the connection from its groups; mirror that in presence
        // and tell the remaining members of every affected branch.
        foreach (var branchId in presence.Disconnect(Context.ConnectionId))
            await BroadcastPresenceAsync(branchId);

        logger.LogInformation(
            "Connection {ConnectionId} closed for {User}. {Reason}",
            Context.ConnectionId, UserName, exception?.Message ?? "client disconnected");

        await base.OnDisconnectedAsync(exception);
    }

    // ---------------------------------------------------------------- groups

    public async Task JoinBranch(string branchId)
    {
        var branch = RequireBranch(branchId);

        await Groups.AddToGroupAsync(Context.ConnectionId, PharmacyGroups.Branch(branch.Id));

        // Only pharmacists count towards presence; doctors and display screens do not.
        if (IsPharmacist && presence.Join(branch.Id, UserName, Context.ConnectionId))
            await BroadcastPresenceAsync(branch.Id);
        else
            await Clients.Caller.PresenceChanged(CurrentPresence(branch.Id));

        logger.LogInformation("{User} joined {Branch}.", UserName, branch.Id);
    }

    public async Task LeaveBranch(string branchId)
    {
        var branch = RequireBranch(branchId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PharmacyGroups.Branch(branch.Id));

        if (IsPharmacist && presence.Leave(branch.Id, UserName, Context.ConnectionId))
            await BroadcastPresenceAsync(branch.Id);

        logger.LogInformation("{User} left {Branch}.", UserName, branch.Id);
    }

    // ---------------------------------------------------------------- queue

    /// <summary>
    /// Atomic claim. Two pharmacists clicking at the same time: one wins, the other gets
    /// "Prescription RX-1024 is already claimed by sara."
    /// </summary>
    [Authorize(Roles = TokenService.PharmacistRole)]
    public async Task<PrescriptionDto> ClaimPrescription(string id)
    {
        var prescription = Run(() => store.Claim(id, UserName));

        await PublishUpdateAsync(prescription);
        return prescription;
    }

    /// <summary>
    /// Status transition with validation. Invalid moves produce a readable HubException,
    /// e.g. "Cannot move RX-1024 from New to Ready."
    /// </summary>
    [Authorize(Roles = TokenService.PharmacistRole)]
    public async Task<PrescriptionDto> UpdateStatus(string id, string status)
    {
        var prescription = Run(() => store.UpdateStatus(id, status, UserName));

        await PublishUpdateAsync(prescription);

        // Private notification: every tab of the doctor who created it.
        if (prescription.Status == PrescriptionStatus.Ready)
            await Clients.User(prescription.CreatedBy).PrescriptionReady(prescription);

        return prescription;
    }

    /// <summary>
    /// Server-to-client streaming: a snapshot immediately, then every 2 seconds.
    /// Cancelling the client subscription cancels this token and ends the stream.
    /// </summary>
    public async IAsyncEnumerable<BranchStatsDto> StreamBranchStats(
        string branchId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var branch = RequireBranch(branchId);

        yield return store.Stats(branch.Id);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken))
            yield return store.Stats(branch.Id);
    }

    // ---------------------------------------------------------------- helpers

    private async Task PublishUpdateAsync(PrescriptionDto prescription)
    {
        await Clients.Group(PharmacyGroups.Branch(prescription.BranchId)).PrescriptionUpdated(prescription);
        await Clients.User(prescription.CreatedBy).PrescriptionUpdated(prescription);
    }

    private Task BroadcastPresenceAsync(string branchId) =>
        Clients.Group(PharmacyGroups.Branch(branchId)).PresenceChanged(CurrentPresence(branchId));

    private BranchPresenceDto CurrentPresence(string branchId) =>
        new(branchId, presence.UsersIn(branchId));

    private BranchDto RequireBranch(string? branchId) =>
        branches.Find(branchId)
        ?? throw new HubException(
            $"Branch '{branchId}' does not exist. Known branches: {branches.KnownBranchIds}.");

    /// <summary>Domain errors are safe to show; everything else is hidden by the hub filter.</summary>
    private static PrescriptionDto Run(Func<PrescriptionDto> action)
    {
        try
        {
            return action();
        }
        catch (PrescriptionException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
