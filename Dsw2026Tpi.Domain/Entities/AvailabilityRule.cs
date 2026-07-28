namespace Dsw2026Tpi.Domain.Entities;

public class AvailabilityRule : EntityBase
{
    public Guid DoctorId { get; init; }
    public Doctor? Doctor { get; private set; }

    public short Year { get; init; }
    public byte Month { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }

    public ICollection<AvailabilitySlot> Slots { get; private set; } = [];

    #region Constructor for EF
#pragma warning disable CS8618
    private AvailabilityRule() { }
#pragma warning restore CS8618
    #endregion

    public AvailabilityRule(Guid doctorId, short year, byte month, DayOfWeek dayOfWeek,
        TimeOnly startTime, TimeOnly endTime, Guid? id = null) : base(id)
    {
        DoctorId = doctorId;
        Year = year;
        Month = month;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }
}