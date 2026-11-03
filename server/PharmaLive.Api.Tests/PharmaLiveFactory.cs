using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Tests;

/// <summary>Boots the real API in-memory (TestServer) so the tests exercise the real pipeline.</summary>
public sealed class PharmaLiveFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Testing");
}

internal static class TestApi
{
    public static async Task<string> LoginAsync(HttpClient http, string userName, string role)
    {
        var response = await http.PostAsJsonAsync("/api/auth/token", new { userName, role });
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token!.AccessToken;
    }

    public static async Task<PrescriptionDto> CreatePrescriptionAsync(
        HttpClient http,
        string accessToken,
        string number,
        string branchId = "AMM-01")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/prescriptions")
        {
            Content = JsonContent.Create(new
            {
                number,
                branchId,
                patientInitials = "A.K",
                items = new[] { "Paracetamol 500mg", "Amoxicillin 250mg" },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/prescriptions failed: {body}");

        return (await response.Content.ReadFromJsonAsync<PrescriptionDto>())!;
    }

    /// <summary>TestServer has no real sockets, so long polling is used instead of WebSockets.</summary>
    public static async Task<HubConnection> ConnectAsync(PharmaLiveFactory factory, string accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/pharmacy"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }
}
