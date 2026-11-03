using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Services;

/// <summary>Seeded branch list. In a real system this comes from the database.</summary>
public sealed class BranchCatalog
{
    private static readonly BranchDto[] Branches =
    [
        new("RAM-01", "Ramallah"),
        new("NAB-01", "Nablus"),
        new("HEB-01", "Hebron"),
    ];

    private readonly Dictionary<string, BranchDto> _byId =
        Branches.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BranchDto> All => Branches;

    public string KnownBranchIds => string.Join(", ", Branches.Select(b => b.Id));

    public BranchDto? Find(string? branchId) =>
        !string.IsNullOrWhiteSpace(branchId) && _byId.TryGetValue(branchId.Trim(), out var branch)
            ? branch
            : null;

    public bool Exists(string? branchId) => Find(branchId) is not null;
}
