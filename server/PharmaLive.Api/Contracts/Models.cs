namespace PharmaLive.Api.Contracts;

public sealed record BranchDto(string Id, string Name);

public static class PrescriptionStatus
{
    public const string New = "New";
    public const string Claimed = "Claimed";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Dispensed = "Dispensed";
    public const string Rejected = "Rejected";

    public static readonly string[] All =
        [New, Claimed, Preparing, Ready, Dispensed, Rejected];

    /// <summary>Returns the canonical casing, or null when the value is not a known status.</summary>
    public static string? Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record PrescriptionDto(
    string Id,
    string Number,
    string BranchId,
    string PatientInitials,
    IReadOnlyList<string> Items,
    string CreatedBy,
    string Status,
    string? ClaimedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? PreparingAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? DispensedAt,
    DateTimeOffset? RejectedAt);

public sealed record BranchPresenceDto(string BranchId, IReadOnlyList<string> Pharmacists);

public sealed record BranchStatsDto(
    string BranchId,
    int New,
    int Claimed,
    int Preparing,
    int Ready,
    int Dispensed,
    int Rejected,
    int Total,
    double? AveragePreparationSeconds,
    DateTimeOffset GeneratedAt);

public sealed record LoginRequest(string? UserName, string? Role);

public sealed record TokenResponse(
    string AccessToken,
    string UserName,
    string Role,
    DateTimeOffset ExpiresAt);

public sealed record CreatePrescriptionRequest(
    string? Number,
    string? BranchId,
    string? PatientInitials,
    IReadOnlyList<string>? Items,
    string? CreatedBy);
