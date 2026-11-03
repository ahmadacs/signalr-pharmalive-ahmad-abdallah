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

        await using var ramallah = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "sara", "Pharmacist"));
        await using var nablus = await TestApi.ConnectAsync(factory, await TestApi.LoginAsync(http, "omar", "Pharmacist"));

        var ramallahReceived = new TaskCompletionSource<PrescriptionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nablusReceived = new TaskCompletionSource<PrescriptionDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        ramallah.On<PrescriptionDto>("PrescriptionCreated", dto =>
        {
            ramallahReceived.TrySetResult(dto);
            return Task.CompletedTask;
        });
        nablus.On<PrescriptionDto>("PrescriptionCreated", dto =>
        {
            nablusReceived.TrySetResult(dto);
            return Task.CompletedTask;
        });

        await ramallah.InvokeAsync("JoinBranch", "RAM-01");
        await nablus.InvokeAsync("JoinBranch", "NAB-01");

        var created = await TestApi.CreatePrescriptionAsync(http, doctorToken, "RX-9002", "RAM-01");

        var received = await ramallahReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(created.Id, received.Id);
        Assert.Equal("RAM-01", received.BranchId);

        // Give Nablus a moment: it must never see a Ramallah prescription.
        await Task.Delay(500);
        Assert.False(nablusReceived.Task.IsCompleted);
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
        Assert.Contains("RAM-01", error.Message);
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
