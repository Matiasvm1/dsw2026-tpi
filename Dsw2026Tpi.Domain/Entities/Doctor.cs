namespace Dsw2026Tpi.Domain.Entities;

public class Doctor : EntityBase
{
    public string Name { get; private set; }
    public string? LicenseNumber { get; private set; }
    public Guid SpecialityId { get; set; }
    public Speciality? Speciality { get; private set; }

    private readonly List<AvailabilityRule> _availabilityRules = [];
    public IReadOnlyCollection<AvailabilityRule> AvailabilityRules => _availabilityRules.AsReadOnly();

    #region Constructor for EF
#pragma warning disable CS8618
    private Doctor()
    {
    }
#pragma warning restore CS8618
    #endregion

    public Doctor(string name, string? licenseNumber, Guid specialityId, Guid? id = null) : base(id)
    {
        Name = name;
        LicenseNumber = licenseNumber;
        SpecialityId = specialityId;
    }

    public void Update(string name, string? licenseNumber, Guid specialityId)
    {
        Name = name;
        LicenseNumber = licenseNumber;
        SpecialityId = specialityId;
    }

    // Experto: el médico conoce sus disponibilidades. Devuelve true si el rango está libre en ese
    // mes. Las reglas dadas de baja no ocupan lugar, aunque vengan cargadas en la colección.
    public bool CheckAvailability(short year, byte month, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        return !_availabilityRules.Any(r =>
            !r.Deleted &&
            r.Year == year &&
            r.Month == month &&
            r.DayOfWeek == dayOfWeek &&
            startTime < r.EndTime &&
            r.StartTime < endTime);
    }

    // Creador: el médico contiene sus disponibilidades, así que las crea.
    public AvailabilityRule AddAvailability(short year, byte month, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        var rule = new AvailabilityRule(Id, year, month, dayOfWeek, startTime, endTime);
        _availabilityRules.Add(rule);
        return rule;
    }
}
