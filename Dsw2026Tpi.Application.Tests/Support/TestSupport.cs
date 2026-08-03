using System.Security.Claims;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Tests.Support;

/// <summary>
/// Helper de reflection para poblar navegaciones con `private set` (Doctor.Speciality,
/// Appointment.AvailabilitySlot, etc.). En producción las carga EF con el Include; en un test
/// unitario no hay EF, así que se inyectan a mano. Es la única forma de armar el grafo completo
/// sin relajar el encapsulamiento de las entidades del dominio.
/// </summary>
public static class Nav
{
    public static T Set<T>(T entity, string property, object? value)
    {
        var prop = typeof(T).GetProperty(property)
            ?? throw new ArgumentException($"No existe la propiedad {property} en {typeof(T).Name}");
        prop.SetValue(entity, value);
        return entity;
    }
}

/// <summary>
/// Fábrica de entidades de dominio para los tests. Valores por defecto sensatos; cada test
/// sobreescribe solo lo que le importa. Las navegaciones se pueblan con <see cref="Nav"/>.
/// </summary>
public static class TestData
{
    public static Speciality Speciality(string name = "Cardiologia", Guid? id = null) =>
        new(name, "Especialidad del corazon", id);

    public static Doctor Doctor(Guid specialityId, Speciality? speciality = null,
        string name = "Dr House", Guid? id = null)
    {
        var doctor = new Doctor(name, "MP12345", specialityId, id);
        if (speciality is not null) Nav.Set(doctor, nameof(Doctor.Speciality), speciality);
        return doctor;
    }

    public static Patient Patient(string dni = "12345678", string? fullName = null, Guid? id = null) =>
        new("user-" + dni, dni, fullName, id);

    public static AvailabilityRule Rule(Guid doctorId, Doctor? doctor = null,
        DayOfWeek day = DayOfWeek.Tuesday, Guid? id = null)
    {
        var rule = new AvailabilityRule(doctorId, 2026, 8, day,
            new TimeOnly(9, 0), new TimeOnly(12, 0), id);
        if (doctor is not null) Nav.Set(rule, nameof(AvailabilityRule.Doctor), doctor);
        return rule;
    }

    /// <summary>
    /// Slot disponible y por defecto FUTURO (mañana 09:00), para no chocar con RN04.
    /// </summary>
    public static AvailabilitySlot Slot(Guid doctorId, AvailabilityRule? rule = null,
        DateOnly? date = null, TimeOnly? start = null, SlotStatus status = SlotStatus.Available,
        Guid? id = null)
    {
        var slotDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var startTime = start ?? new TimeOnly(9, 0);
        var slot = new AvailabilitySlot(rule?.Id ?? Guid.NewGuid(), doctorId, slotDate,
            startTime, startTime.AddMinutes(30), id);
        if (status == SlotStatus.Booked) slot.Book();
        else if (status == SlotStatus.Blocked) slot.Block();
        if (rule is not null) Nav.Set(slot, nameof(AvailabilitySlot.AvailabilityRule), rule);
        return slot;
    }

    /// <summary>
    /// Turno con TODO el grafo poblado (slot -> rule -> doctor -> speciality, y patient), tal como
    /// llegaría de un GetFiltered/Paginate con el FullGraph. Para probar el mapeo del Par B.
    /// </summary>
    public static Appointment BookedAppointment(
        Patient patient, Doctor doctor, Speciality speciality,
        DateOnly? date = null, TimeOnly? start = null, string reason = "consulta general",
        Guid? id = null)
    {
        var rule = Rule(doctor.Id, doctor);
        var slot = Slot(doctor.Id, rule, date, start);
        var appt = new Appointment(slot.Id, patient.Id, reason, id);
        Nav.Set(appt, nameof(Appointment.AvailabilitySlot), slot);
        Nav.Set(appt, nameof(Appointment.Patient), patient);
        return appt;
    }
}

/// <summary>
/// Fábrica de <see cref="ClaimsPrincipal"/> que espeja lo que arma el JwtService: el claim de rol
/// lleva el nombre INTERNO (Roles.Patient = "Paciente"), y el paciente lleva dni + patientId.
/// </summary>
public static class Principals
{
    public static ClaimsPrincipal Patient(string dni = "12345678", Guid? patientId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, Roles.Patient),
            new(CustomClaims.Dni, dni),
        };
        if (patientId.HasValue) claims.Add(new Claim(CustomClaims.PatientId, patientId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static ClaimsPrincipal Admin()
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, Roles.Administrator) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
