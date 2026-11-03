using PharmaLive.Api.Auth;
using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", (LoginRequest request, TokenService tokens) =>
        {
            var errors = new Dictionary<string, string[]>();

            var userName = request.UserName?.Trim() ?? string.Empty;
            if (userName.Length == 0)
            {
                errors["userName"] = ["User name is required."];
            }
            else if (userName.Length > 30)
            {
                errors["userName"] =
                    [$"User name '{userName}' is too long. Use 30 characters or fewer."];
            }
            else if (!userName.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                errors["userName"] =
                    [$"User name '{userName}' contains invalid characters. Use letters, digits, '.', '-' or '_'."];
            }

            var role = TokenService.NormalizeRole(request.Role);
            if (role is null)
            {
                errors["role"] =
                    [$"Role '{request.Role}' is not valid. Choose one of: {string.Join(", ", TokenService.Roles)}."];
            }

            return errors.Count > 0
                ? Results.ValidationProblem(errors)
                : Results.Ok(tokens.Issue(userName, role!));
        });

        return app;
    }
}
