using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using PharmaLive.Api.Auth;
using PharmaLive.Api.Endpoints;
using PharmaLive.Api.Hubs;
using PharmaLive.Api.Hubs.Filters;
using PharmaLive.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------------------
// CORS - the SignalR client sends credentials, so AllowAnyOrigin() is not usable.
// ---------------------------------------------------------------------------
builder.Services.AddCors(options => options.AddPolicy("client", policy => policy
    .SetIsOriginAllowed(_ => true) // dev only: the Angular app runs on another port
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---------------------------------------------------------------------------
// Authentication - JWT bearer.
// Browsers cannot set an Authorization header on WebSockets/SSE, so the client sends the
// token as ?access_token=... We read it ONLY for hub paths, never for REST endpoints.
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep "sub", "name" and "role" exactly as issued

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = TokenService.KeyFrom(config),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// SignalR
// ---------------------------------------------------------------------------
builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.MaximumReceiveMessageSize = 64 * 1024;
        options.AddFilter<HubLoggingFilter>();
    })
    .AddJsonProtocol(protocol =>
        protocol.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

// All state is singleton: hubs themselves are transient and must stay stateless.
builder.Services.AddSingleton<BranchCatalog>();
builder.Services.AddSingleton<PrescriptionStore>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<HubLoggingFilter>();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

var app = builder.Build();

// Order matters: CORS -> Authentication -> Authorization -> endpoints.
app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (BranchCatalog branches) => Results.Ok(new
{
    service = "PharmaLive.Api",
    hub = "/hubs/pharmacy",
    branches = branches.All.Select(branch => branch.Id),
}));

app.MapAuthEndpoints();
app.MapBranchEndpoints();
app.MapPrescriptionEndpoints();

app.MapHub<PharmacyHub>("/hubs/pharmacy", options =>
{
    // .NET 8+ stateful reconnect: a short drop resumes the same connection (group membership survives).
    options.AllowStatefulReconnects = true;
});

app.Run();

/// <summary>Exposed so the integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
