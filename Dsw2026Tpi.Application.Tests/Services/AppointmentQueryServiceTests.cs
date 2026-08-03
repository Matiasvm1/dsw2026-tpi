using System.Linq.Expressions;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.Application.Tests.Support;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Moq;

namespace Dsw2026Tpi.Application.Tests.Services;

/// <summary>
/// Tests de las CONSULTAS de turnos (Par B). El predicado de filtrado lo ejecuta EF en producción;
/// acá el mock devuelve la lista que queramos, así que probamos la autorización (D20), el parseo de
/// fecha, el orden y el mapeo del grafo (que doctor/especialidad/paciente NO vuelvan en null).
/// </summary>
public class AppointmentQueryServiceTests
{
    private readonly Mock<IPersistence> _persistence = new();
    private readonly AppointmentQueryService _sut;

    public AppointmentQueryServiceTests()
    {
        _sut = new AppointmentQueryService(_persistence.Object);
    }

    private void SetupGetFiltered(IEnumerable<Appointment>? result) =>
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<Appointment, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(result);

    private void SetupPaginate<TKey>(Pagination<Appointment> page) =>
        _persistence.Setup(p => p.Paginate(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<Appointment, bool>>>(),
                It.IsAny<Expression<Func<Appointment, TKey>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync(page);

    // ---------------------------------------------------------------- GetByPatient

    // D20: un paciente pidiendo el DNI de otro se lo come con un 403.
    [Fact]
    public async Task GetByPatient_PacienteConsultaDniAjeno_Lanza403()
    {
        var user = Principals.Patient("12345678");

        var ex = await Assert.ThrowsAsync<AuthorizationException>(
            () => _sut.GetByPatient("87654321", user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_FORBIDDEN), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetByPatient_PacienteConsultaSuPropioDni_DevuelveSusTurnos()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        SetupGetFiltered(new[] { TestData.MakeBookedAppointment(patient, doctor, speciality) });

        var result = (await _sut.GetByPatient("12345678", Principals.Patient("12345678"))).ToList();

        Assert.Single(result);
        // El grafo tiene que venir poblado (el bug más común del Par B es que salga en null).
        Assert.Equal(doctor.Name, result[0].Doctor.Name);
        Assert.Equal("Cardiologia", result[0].Specialty.Name);
        Assert.Equal("12345678", result[0].Patient.Dni);
    }

    [Fact]
    public async Task GetByPatient_Admin_ConsultaCualquierDni_SinRestriccion()
    {
        SetupGetFiltered(Array.Empty<Appointment>());

        var result = await _sut.GetByPatient("99999999", Principals.Admin());

        Assert.Empty(result); // no lanza 403
    }

    // DNI sin turnos -> [] con 200 (GetFiltered puede devolver null).
    [Fact]
    public async Task GetByPatient_SinResultados_DevuelveListaVacia()
    {
        SetupGetFiltered(null);

        var result = await _sut.GetByPatient("12345678", Principals.Patient("12345678"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByPatient_OrdenaPorFechaYHora()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var tarde = TestData.MakeBookedAppointment(patient, doctor, speciality,
            date: new DateOnly(2026, 8, 20), start: new TimeOnly(15, 0));
        var temprano = TestData.MakeBookedAppointment(patient, doctor, speciality,
            date: new DateOnly(2026, 8, 10), start: new TimeOnly(9, 0));
        // Se entregan desordenados a propósito.
        SetupGetFiltered(new[] { tarde, temprano });

        var result = (await _sut.GetByPatient("12345678", Principals.Patient("12345678"))).ToList();

        Assert.Equal("2026-08-10", result[0].Date);
        Assert.Equal("2026-08-20", result[1].Date);
    }

    // ---------------------------------------------------------------- GetByDate

    [Fact]
    public async Task GetByDate_FechaConFormatoInvalido_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.GetByDate("2026-13-45", new PaginationQuery(null, null)));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_DATE_INVALID), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetByDate_SinFecha_UsaHoyYDevuelvePaginado()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var appt = TestData.MakeBookedAppointment(patient, doctor, speciality);
        SetupPaginate<TimeOnly>(new Pagination<Appointment>(10, 1, 1, new[] { appt }));

        var result = await _sut.GetByDate(null, new PaginationQuery(null, null));

        Assert.Equal(1, result.Total);
        Assert.Single(result.Data);
    }

    [Fact]
    public async Task GetByDate_FechaValida_MapeaLaRespuesta()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var appt = TestData.MakeBookedAppointment(patient, doctor, speciality);
        SetupPaginate<TimeOnly>(new Pagination<Appointment>(10, 1, 1, new[] { appt }));

        var result = await _sut.GetByDate("2026-08-10", new PaginationQuery(null, null));

        Assert.Equal("BOOKED", result.Data.First().Status);
    }

    // ---------------------------------------------------------------- Search

    [Fact]
    public async Task Search_FechaInvalida_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.Search(null, null, null, "no-es-fecha", new PaginationQuery(null, null)));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_DATE_INVALID), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Search_DevuelveSearchResponseConAvailableTimeFormateado()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var appt = TestData.MakeBookedAppointment(patient, doctor, speciality,
            date: new DateOnly(2026, 8, 10), start: new TimeOnly(9, 30));
        SetupPaginate<DateOnly>(new Pagination<Appointment>(10, 1, 1, new[] { appt }));

        var result = await _sut.Search(null, doctor.Id, null, null, new PaginationQuery(null, null));

        Assert.Equal("2026-08-10 09:30", result.Data.First().AvailableTime);
    }
}
