namespace Dsw2026Tpi.Domain.Entities;

public class Appointment : EntityBase
{
    public Guid AvailabilitySlotId { get; init; }
    public AvailabilitySlot? AvailabilitySlot { get; private set; }

    public Guid PatientId { get; init; }
    public Patient? Patient { get; private set; }

    public string Reason { get; init; }
    public AppointmentStatus Status { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? AttendedAt { get; private set; }

    public byte[]? RowVersion { get; init; }

    #region Constructor for EF
#pragma warning disable CS8618
    private Appointment() { }
#pragma warning restore CS8618
    #endregion

    public Appointment(Guid availabilitySlotId, Guid patientId, string reason, Guid? id = null) : base(id)
    {
        AvailabilitySlotId = availabilitySlotId;
        PatientId = patientId;
        Reason = reason;
        Status = AppointmentStatus.Booked;
    }

    public void Cancel()
    {
        Status = AppointmentStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }

    public void MarkAsAttended()
    {
        Status = AppointmentStatus.Attended;
        AttendedAt = DateTime.UtcNow;
    }

    public void MarkAsNoShow() => Status = AppointmentStatus.NoShow;
}
