using System.Text.RegularExpressions;

namespace PharmaLive.Api.Contracts;

public static partial class ValidationRules
{
    public const int MaxItems = 10;
    public const int MaxItemLength = 120;

    [GeneratedRegex(@"^RX-\d{4,6}$", RegexOptions.IgnoreCase)]
    public static partial Regex PrescriptionNumber { get; }

    [GeneratedRegex(@"^[A-Za-z]{2,4}$")]
    public static partial Regex PatientInitials { get; }
}
