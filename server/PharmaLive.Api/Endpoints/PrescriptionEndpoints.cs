using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using PharmaLive.Api.Auth;
using PharmaLive.Api.Contracts;
using PharmaLive.Api.Hubs;
using PharmaLive.Api.Services;

namespace PharmaLive.Api.Endpoints;

public static class PrescriptionEndpoints
{
    public static IEndpointRouteBuilder MapPrescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prescriptions").RequireAuthorization();

        // Initial page load for a branch queue.
        group.MapGet("/", (string? branchId, PrescriptionStore store, BranchCatalog branches) =>
        {
            var branch = branches.Find(branchId);
            if (branch is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["branchId"] =
                        [$"Branch '{branchId}' does not exist. Known branches: {branches.KnownBranchIds}."],
                });
            }

            return Results.Ok(store.ForBranch(branch.Id));
        });

        // The doctor is not a member of any branch group, so "my prescriptions" needs its own feed.
        group.MapGet("/mine", (ClaimsPrincipal user, PrescriptionStore store) =>
            Results.Ok(store.ForCreator(Claim(user, JwtRegisteredClaimNames.Sub))));

        group.MapGet("/next-number", (PrescriptionStore store) =>
            Results.Ok(new { number = store.NextNumber() }));

        group.MapPost("/", CreatePrescriptionAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = TokenService.DoctorRole });

        return app;
    }

    private static async Task<IResult> CreatePrescriptionAsync(
        CreatePrescriptionRequest request,
        ClaimsPrincipal user,
        PrescriptionStore store,
        BranchCatalog branches,
        IHubContext<PharmacyHub, IPharmacyClient> hub)
    {
        var errors = Validate(request, branches);

        var branch = branches.Find(request.BranchId);
        var number = request.Number?.Trim().ToUpperInvariant() ?? string.Empty;
        var initials = (request.PatientInitials ?? string.Empty).Replace(".", string.Empty).Trim().ToUpperInvariant();

        // Uniqueness can only be checked inside the store, but the error still belongs to "number".
        if (errors.Count == 0 && store.ForBranch(branch!.Id).Any(p =>
                p.Number.Equals(number, StringComparison.OrdinalIgnoreCase)))
        {
            errors["number"] = [$"Prescription number '{number}' already exists."];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // createdBy comes from the token, never from the body: a doctor cannot create work in someone else's name.
        var createdBy = Claim(user, JwtRegisteredClaimNames.Sub);

        PrescriptionDto prescription;
        try
        {
            prescription = store.Create(
                number,
                branch!.Id,
                initials,
                request.Items!.Select(item => item.Trim()).ToArray(),
                createdBy);
        }
        catch (PrescriptionException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["number"] = [ex.Message],
            });
        }

        // Pushed from OUTSIDE the hub, exactly like the background job pattern: IHubContext.
        await hub.Clients.Group(PharmacyGroups.Branch(prescription.BranchId))
            .PrescriptionCreated(prescription);
        await hub.Clients.User(createdBy).PrescriptionCreated(prescription);

        return Results.Created($"/api/prescriptions/{prescription.Id}", prescription);
    }

    private static Dictionary<string, string[]> Validate(CreatePrescriptionRequest request, BranchCatalog branches)
    {
        var errors = new Dictionary<string, string[]>();

        var number = request.Number?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            errors["number"] = ["Prescription number is required, e.g. RX-1024."];
        }
        else if (!ValidationRules.PrescriptionNumber.IsMatch(number))
        {
            errors["number"] =
                [$"Prescription number '{number}' is invalid. Use RX- followed by 4 to 6 digits, e.g. RX-1024."];
        }

        if (string.IsNullOrWhiteSpace(request.BranchId))
        {
            errors["branchId"] = [$"Branch is required. Choose one of: {branches.KnownBranchIds}."];
        }
        else if (!branches.Exists(request.BranchId))
        {
            errors["branchId"] =
                [$"Branch '{request.BranchId}' does not exist. Known branches: {branches.KnownBranchIds}."];
        }

        var initials = (request.PatientInitials ?? string.Empty).Replace(".", string.Empty).Trim();
        if (initials.Length == 0)
        {
            errors["patientInitials"] = ["Patient initials are required, e.g. A.K."];
        }
        else if (!ValidationRules.PatientInitials.IsMatch(initials))
        {
            errors["patientInitials"] =
                [$"Patient initials '{request.PatientInitials}' are invalid. Use 2 to 4 letters, e.g. A.K."];
        }

        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            errors["items"] = ["At least one medicine item is required."];
        }
        else if (items.Count > ValidationRules.MaxItems)
        {
            errors["items"] =
                [$"A prescription can contain at most {ValidationRules.MaxItems} items."];
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i]?.Trim() ?? string.Empty;
                if (item.Length == 0)
                {
                    errors[$"items[{i}]"] = [$"Item {i + 1} is empty. Enter a medicine name."];
                }
                else if (item.Length > ValidationRules.MaxItemLength)
                {
                    errors[$"items[{i}]"] =
                        [$"Item {i + 1} is {item.Length} characters long. Use {ValidationRules.MaxItemLength} characters or fewer."];
                }
            }
        }

        return errors;
    }

    private static string Claim(ClaimsPrincipal user, string type) =>
        user.FindFirstValue(type)
        ?? throw new InvalidOperationException($"The access token has no '{type}' claim.");
}
