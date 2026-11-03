using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Hubs;

/// <summary>
/// Strongly-typed client contract. No magic method-name strings anywhere on the server:
/// the compiler fails the build if a method is renamed.
/// </summary>
public interface IPharmacyClient
{
    Task PrescriptionCreated(PrescriptionDto prescription);

    Task PrescriptionUpdated(PrescriptionDto prescription);

    /// <summary>Private notification, sent only to the doctor who created the prescription.</summary>
    Task PrescriptionReady(PrescriptionDto prescription);

    Task PresenceChanged(BranchPresenceDto presence);
}
