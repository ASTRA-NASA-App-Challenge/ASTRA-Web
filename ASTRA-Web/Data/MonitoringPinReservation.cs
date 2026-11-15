namespace Data;

// Retain the keyed lookup permanently; encrypted cleanup details are removed after delivery.
public sealed class MonitoringPinReservation
{
    public required string PinLookup { get; set; }
    public string? ProtectedPin { get; set; }
    public string? ProtectionId { get; set; }
    public bool DeletionPending { get; set; }
}
