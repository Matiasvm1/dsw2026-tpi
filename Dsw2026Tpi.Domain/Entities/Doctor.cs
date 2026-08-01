namespace Dsw2026Tpi.Domain.Entities;

public class Doctor : EntityBase
{
    public string Name { get; private set; }
    public string? LicenseNumber { get; private set; }
    public Guid SpecialityId { get; set; }
    public Speciality? Speciality { get; private set; }

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
}
