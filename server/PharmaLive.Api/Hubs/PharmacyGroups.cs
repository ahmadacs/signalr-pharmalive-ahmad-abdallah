namespace PharmaLive.Api.Hubs;

public static class PharmacyGroups
{
    private const string Prefix = "branch:";

    /// <summary>Group name for a branch. Always call with the canonical id from BranchCatalog.</summary>
    public static string Branch(string branchId) => $"{Prefix}{branchId.ToUpperInvariant()}";
}
