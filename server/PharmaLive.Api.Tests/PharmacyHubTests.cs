using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Tests;

public sealed class PharmacyHubTests
{
    [Fact]
    public async Task Two_pharmacists_claiming_at_the_same_time_leave_exactly_one_winner()
    {
        using var factory = new PharmaLiveFactory();
        using var http = factory.CreateClient();

        var doctorToken = await TestApi.LoginAsync(http, "dr.khaled", "Doctor");
        var prescription = await TestApi.CreatePrescriptionAsync(http, doctorToken, "RX-9001");

        await using var sara = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "sara", "Pharmacist"));
        await using var omar = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "omar", "Pharmacist"));

        // Both invocations start before either is awaited: a real race, not a sequence.
        var results = await Task.WhenAll(
            TryClaimAsync(sara, prescription.Id),
            TryClaimAsync(omar, prescription.Id));

        var winner = Assert.Single(results, r => r.Success);
        var loser = Assert.Single(results, r => !r.Success);

        Assert.Contains(prescription.Number, loser.Error);
        Assert.Contains("already claimed by", loser.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(winner.ClaimedBy!, loser.Error);
    }

    [Fact]
    public async Task Created_over_http_reaches_only_the_branch_group()
    {
        using var factory = new PharmaLiveFactory();
        using var http = factory.CreateClient();

        var doctorToken = await TestApi.LoginAsync(http, "dr.khaled", "Doctor");

        await using var amman = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "sara", "Pharmacist"));
        await using var irbid = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "omar", "Pharmacist"));

        var ammanReceived = new TaskCompletionSource<PrescriptionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var irbidReceived = new TaskCompletionSource<PrescriptionDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        amman.On<PrescriptionDto>("PrescriptionCreated", dto =>
        {
            ammanReceived.TrySetResult(dto);
            return Task.CompletedTask;
        });
        irbid.On<PrescriptionDto>("PrescriptionCreated", dto =>
        {
            irbidReceived.TrySetResult(dto);
            return Task.CompletedTask;
        });

        await amman.InvokeAsync("JoinBranch", "AMM-01");
        await irbid.InvokeAsync("JoinBranch", "IRB-01");

        var created = await TestApi.CreatePrescriptionAsync(http, doctorToken, "RX-9002", "AMM-01");

        var received = await ammanReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(created.Id, received.Id);
        Assert.Equal("AMM-01", received.BranchId);

        // Give Irbid a moment: it must never see an Amman prescription.
        await Task.Delay(500);
        Assert.False(irbidReceived.Task.IsCompleted);
    }

    [Fact]
    public async Task Invalid_transition_returns_a_readable_error()
    {
        using var factory = new PharmaLiveFactory();
        using var http = factory.CreateClient();

        var doctorToken = await TestApi.LoginAsync(http, "dr.khaled", "Doctor");
        var prescription = await TestApi.CreatePrescriptionAsync(http, doctorToken, "RX-9003");

        await using var sara = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "sara", "Pharmacist"));

        var error = await Assert.ThrowsAsync<HubException>(() =>
            sara.InvokeAsync<PrescriptionDto>("UpdateStatus", prescription.Id, "Ready"));

        Assert.Contains($"Cannot move {prescription.Number} from New to Ready.", error.Message);
    }

    [Fact]
    public async Task Joining_an_unknown_branch_returns_a_readable_error()
    {
        using var factory = new PharmaLiveFactory();
        using var http = factory.CreateClient();

        await using var sara = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "sara", "Pharmacist"));

        var error = await Assert.ThrowsAsync<HubException>(() =>
            sara.InvokeAsync("JoinBranch", "XYZ-99"));

        Assert.Contains("Branch 'XYZ-99' does not exist.", error.Message);
        Assert.Contains("AMM-01", error.Message);
    }

    private static async Task<ClaimAttempt> TryClaimAsync(HubConnection connection, string prescriptionId)
    {
        try
        {
            var claimed = await connection.InvokeAsync<PrescriptionDto>("ClaimPrescription", prescriptionId);
            return new ClaimAttempt(true, claimed.ClaimedBy ?? string.Empty, string.Empty);
        }
        catch (HubException ex)
        {
            return new ClaimAttempt(false, null, ex.Message);
        }
    }

    private sealed record ClaimAttempt(bool Success, string? ClaimedBy, string Error);
}
