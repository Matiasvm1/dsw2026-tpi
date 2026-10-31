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

    private readonly List<AvailabilitySlot> _slots = [];
    public IReadOnlyCollection<AvailabilitySlot> Slots => _slots.AsReadOnly();

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

    // Creador: la disponibilidad contiene los turnos, así que los crea. Devuelve los recién creados.
    public IReadOnlyList<AvailabilitySlot> GenerateSlots(
        DateOnly from, DateTime nowUtc, IReadOnlySet<DateOnly> nonWorkingDays,
        IReadOnlySet<(DateOnly Date, TimeOnly Start)>? occupied = null)
    {
        var lastDay = DateTime.DaysInMonth(Year, Month);
        var slots = new List<AvailabilitySlot>();

        for (var day = from.Day; day <= lastDay; day++)
        {
            var date = new DateOnly(Year, Month, day);
            if (date.DayOfWeek != DayOfWeek) continue;

            if (nonWorkingDays.Contains(date)) continue;

            for (var start = StartTime; start < EndTime; start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(30);

                if (date.ToDateTime(start) <= nowUtc) continue;

                // No regenerar un horario que ya sostiene un slot vivo (reservado o pasado).
                if (occupied is not null && occupied.Contains((date, start))) continue;

                slots.Add(new AvailabilitySlot(Id, DoctorId, date, start, end));
            }
        }

        _slots.AddRange(slots);
        return slots;
    }
}