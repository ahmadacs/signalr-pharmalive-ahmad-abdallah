using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Services;

/// <summary>A readable, client-safe domain error (turned into HubException / ValidationProblem).</summary>
public sealed class PrescriptionException(string message) : Exception(message);

internal sealed class Prescription
{
    public required string Id { get; init; }
    public required string Number { get; init; }
    public required string BranchId { get; init; }
    public required string PatientInitials { get; init; }
    public required IReadOnlyList<string> Items { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public string Status { get; set; } = PrescriptionStatus.New;
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? PreparingAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? DispensedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }

    public PrescriptionDto ToDto() => new(
        Id,
        Number,
        BranchId,
        PatientInitials,
        Items,
        CreatedBy,
        Status,
        ClaimedBy,
        CreatedAt,
        ClaimedAt,
        PreparingAt,
        ReadyAt,
        DispensedAt,
        RejectedAt);
}

/// <summary>
/// In-memory prescription queue. Every read/write goes through a single lock: the store is small,
/// and this is what makes the claim race (two pharmacists clicking at once) deterministic.
/// </summary>
public sealed class PrescriptionStore
{
    private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.Ordinal)
    {
        [PrescriptionStatus.New] = [PrescriptionStatus.Claimed, PrescriptionStatus.Rejected],
        [PrescriptionStatus.Claimed] = [PrescriptionStatus.Preparing, PrescriptionStatus.Rejected],
        [PrescriptionStatus.Preparing] = [PrescriptionStatus.Ready, PrescriptionStatus.Rejected],
        [PrescriptionStatus.Ready] = [PrescriptionStatus.Dispensed, PrescriptionStatus.Rejected],
        [PrescriptionStatus.Dispensed] = [],
        [PrescriptionStatus.Rejected] = [],
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, Prescription> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idByNumber = new(StringComparer.OrdinalIgnoreCase);

    public PrescriptionDto Create(
        string number,
        string branchId,
        string patientInitials,
        IReadOnlyList<string> items,
        string createdBy)
    {
        lock (_gate)
        {
            if (_idByNumber.ContainsKey(number))
                throw new PrescriptionException($"Prescription number '{number}' already exists.");

            var prescription = new Prescription
            {
                Id = Guid.NewGuid().ToString("N"),
                Number = number,
                BranchId = branchId,
                PatientInitials = patientInitials,
                Items = items,
                CreatedBy = createdBy,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _byId[prescription.Id] = prescription;
            _idByNumber[prescription.Number] = prescription.Id;
            return prescription.ToDto();
        }
    }

    public IReadOnlyList<PrescriptionDto> ForBranch(string branchId)
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(p => p.BranchId.Equals(branchId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.ToDto())
                .ToArray();
        }
    }

    public IReadOnlyList<PrescriptionDto> ForCreator(string userName)
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(p => p.CreatedBy.Equals(userName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.ToDto())
                .ToArray();
        }
    }

    public PrescriptionDto Get(string idOrNumber)
    {
        lock (_gate)
        {
            return (FindLocked(idOrNumber) ?? throw NotFound(idOrNumber)).ToDto();
        }
    }

    /// <summary>
    /// Atomically claims a prescription. Exactly one caller can win when two run concurrently:
    /// the whole check-and-set happens inside the lock, so the loser sees the winner's name.
    /// </summary>
    public PrescriptionDto Claim(string idOrNumber, string userName)
    {
        lock (_gate)
        {
            var prescription = FindLocked(idOrNumber) ?? throw NotFound(idOrNumber);

            if (prescription.ClaimedBy is not null)
            {
                throw new PrescriptionException(
                    $"Prescription {prescription.Number} is already claimed by {prescription.ClaimedBy}.");
            }

            if (prescription.Status != PrescriptionStatus.New)
            {
                throw new PrescriptionException(
                    $"Prescription {prescription.Number} is {prescription.Status} and cannot be claimed.");
            }

            prescription.Status = PrescriptionStatus.Claimed;
            prescription.ClaimedBy = userName;
            prescription.ClaimedAt = DateTimeOffset.UtcNow;
            return prescription.ToDto();
        }
    }

    public PrescriptionDto UpdateStatus(string idOrNumber, string? newStatus, string userName)
    {
        lock (_gate)
        {
            var prescription = FindLocked(idOrNumber) ?? throw NotFound(idOrNumber);

            var status = PrescriptionStatus.Normalize(newStatus);
            if (status is null)
            {
                throw new PrescriptionException(
                    $"'{newStatus}' is not a valid status for {prescription.Number}. " +
                    $"Valid statuses: {string.Join(", ", PrescriptionStatus.All)}.");
            }

            var allowed = AllowedTransitions.GetValueOrDefault(prescription.Status) ?? [];
            if (!allowed.Contains(status, StringComparer.Ordinal))
            {
                throw new PrescriptionException(
                    $"Cannot move {prescription.Number} from {prescription.Status} to {status}.");
            }

            if (prescription.ClaimedBy is not null &&
                !prescription.ClaimedBy.Equals(userName, StringComparison.OrdinalIgnoreCase))
            {
                throw new PrescriptionException(
                    $"{prescription.Number} is claimed by {prescription.ClaimedBy}. " +
                    $"Only {prescription.ClaimedBy} can move it from {prescription.Status}.");
            }

            var now = DateTimeOffset.UtcNow;
            prescription.Status = status;
            switch (status)
            {
                case PrescriptionStatus.Preparing: prescription.PreparingAt = now; break;
                case PrescriptionStatus.Ready: prescription.ReadyAt = now; break;
                case PrescriptionStatus.Dispensed: prescription.DispensedAt = now; break;
                case PrescriptionStatus.Rejected: prescription.RejectedAt = now; break;
            }

            return prescription.ToDto();
        }
    }

    public string NextNumber()
    {
        lock (_gate)
        {
            var highest = 1000;
            foreach (var number in _idByNumber.Keys)
            {
                if (number.StartsWith("RX-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(number.AsSpan(3), out var value) &&
                    value > highest)
                {
                    highest = value;
                }
            }

            return $"RX-{highest + 1}";
        }
    }

    public BranchStatsDto Stats(string branchId)
    {
        lock (_gate)
        {
            var prescriptions = _byId.Values
                .Where(p => p.BranchId.Equals(branchId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            int Count(string status) => prescriptions.Count(p => p.Status == status);

            var completed = prescriptions
                .Where(p => p.ClaimedAt is not null && p.ReadyAt is not null)
                .ToArray();

            double? averagePreparationSeconds = completed.Length == 0
                ? null
                : Math.Round(completed.Average(p => (p.ReadyAt!.Value - p.ClaimedAt!.Value).TotalSeconds), 1);

            return new BranchStatsDto(
                branchId,
                Count(PrescriptionStatus.New),
                Count(PrescriptionStatus.Claimed),
                Count(PrescriptionStatus.Preparing),
                Count(PrescriptionStatus.Ready),
                Count(PrescriptionStatus.Dispensed),
                Count(PrescriptionStatus.Rejected),
                prescriptions.Length,
                averagePreparationSeconds,
                DateTimeOffset.UtcNow);
        }
    }

    private Prescription? FindLocked(string? idOrNumber)
    {
        if (string.IsNullOrWhiteSpace(idOrNumber)) return null;

        var key = idOrNumber.Trim();
        if (_byId.TryGetValue(key, out var byId)) return byId;

        return _idByNumber.TryGetValue(key, out var id) && _byId.TryGetValue(id, out var byNumber)
            ? byNumber
            : null;
    }

    private static PrescriptionException NotFound(string? idOrNumber) =>
        new($"Prescription '{idOrNumber}' does not exist.");
}
