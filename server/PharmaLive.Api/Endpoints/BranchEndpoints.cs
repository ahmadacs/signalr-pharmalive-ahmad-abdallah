using PharmaLive.Api.Contracts;
using PharmaLive.Api.Services;

namespace PharmaLive.Api.Endpoints;

public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/branches", (BranchCatalog branches) => Results.Ok(branches.All))
            .RequireAuthorization();

        return app;
    }
}
