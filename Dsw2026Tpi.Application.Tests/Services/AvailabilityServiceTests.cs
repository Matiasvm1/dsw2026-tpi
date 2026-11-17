using System.Linq.Expressions;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.Application.Tests.Support;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dsw2026Tpi.Application.Tests.Services;

/// <summary>
/// Tests de la generación de disponibilidad: existencia del médico, días requeridos, y todas las
/// reglas de parseo (día en español, horario "HH:mm" alineado a 30', rango válido y sin
/// solapamientos ni con el mismo payload ni con reglas ya cargadas).
/// </summary>
public class AvailabilityServiceTests
{
    private readonly Mock<IPersistence> _persistence = new();
    private readonly AvailabilityService _sut;

    public AvailabilityServiceTests()
    {
        var config = new ConfigurationBuilder().Build(); // sin NonWorkingDays -> conjunto vacío
        _sut = new AvailabilityService(_persistence.Object, config, Mock.Of<ILogger<AvailabilityService>>());
    }

    private static AvailabilityModel.Request Req(Guid doctorId,
        params (string day, string start, string end)[] days) =>
        new(doctorId, days.Select(d => new AvailabilityModel.DayRequest(d.day, d.start, d.end)).ToList());

    private Doctor DoctorExists()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(doctor);
        _persistence.Setup(p => p.GetDoctorWithAvailabilityRules(It.IsAny<Guid>(), It.IsAny<short>(), It.IsAny<byte>()))
            .ReturnsAsync(doctor);
        return doctor;
    }

    private void ExistingRules(params AvailabilityRule[] rules) =>
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<AvailabilityRule, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(rules);

    [Fact]
    public async Task Create_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetDoctorWithAvailabilityRules(It.IsAny<Guid>(), It.IsAny<short>(), It.IsAny<byte>()))
            .ReturnsAsync((Doctor?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Create(Req(Guid.NewGuid(), ("MARTES", "09:00", "12:00"))));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_SinDias_Lanza400()
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(Req(Guid.NewGuid())));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_DAYS_REQUIRED), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_DiaInvalido_Lanza400()
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.Create(Req(Guid.NewGuid(), ("FUNESDAY", "09:00", "12:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_INVALID_DAY), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_HoraConFormatoInvalido_Lanza400()
    {
        DoctorExists();

        // "9:00" no cumple "HH:mm" (hora de un dígito) -> AVAILABILITY_INVALID_TIME.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.Create(Req(Guid.NewGuid(), ("MARTES", "9:00", "12:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_INVALID_TIME), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_RangoInvertido_Lanza400()
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.Create(Req(Guid.NewGuid(), ("MARTES", "10:00", "09:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_INVALID_RANGE), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_HorarioNoAlineadoA30Minutos_Lanza400()
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.Create(Req(Guid.NewGuid(), ("MARTES", "09:15", "12:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_NOT_ALIGNED), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_DosFranjasDelMismoDiaSeSolapan_Lanza409()
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.Create(Req(Guid.NewGuid(),
                ("MARTES", "09:00", "11:00"),
                ("MARTES", "10:00", "12:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_OVERLAP), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_SeSolapaConReglaExistente_Lanza409YNoPersisteNada()
    {
        var doctor = DoctorExists();
        var now = DateTime.UtcNow;
        // Ya hay una regla MARTES 09:00-12:00 cargada para ese médico en el mes actual.
        doctor.AddAvailability((short)now.Year, (byte)now.Month, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(12, 0));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.Create(Req(doctor.Id, ("MARTES", "10:00", "11:00"))));

        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_OVERLAP), ex.Error.ErrorCode);
        _persistence.Verify(p => p.AddRange(It.IsAny<IEnumerable<AvailabilityRule>>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConDatosValidos_ElMedicoCreaLaReglaYSeGuardaTodoDeUnaVez()
    {
        var doctor = DoctorExists();
        List<AvailabilityRule>? saved = null;
        _persistence.Setup(p => p.AddRange(It.IsAny<IEnumerable<AvailabilityRule>>()))
            .Callback<IEnumerable<AvailabilityRule>>(rules => saved = rules.ToList())
            .Returns(Task.CompletedTask);

        var result = await _sut.Create(Req(doctor.Id, ("MARTES", "09:00", "12:00")));

        Assert.Single(result.Days); // devuelve el schedule creado (1 día)
        Assert.Single(doctor.AvailabilityRules);
        Assert.Same(doctor.AvailabilityRules.Single(), Assert.Single(saved!));
        _persistence.Verify(p => p.AddRange(It.IsAny<IEnumerable<AvailabilityRule>>()), Times.Once);
        _persistence.Verify(p => p.Add(It.IsAny<AvailabilityRule>()), Times.Never);
        _persistence.Verify(p => p.Add(It.IsAny<AvailabilitySlot>()), Times.Never);
    }

    private void ExistingSlots(params AvailabilitySlot[] slots) =>
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<AvailabilitySlot, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(slots);

    // AVL-11: el PUT sobreescribe la disponibilidad NO reservada futura, pero conserva los turnos
    // reservados (y la regla que los sostiene) en lugar de rechazar el reemplazo.
    [Fact]
    public async Task Update_ConSlotReservado_ConservaElReservadoYReemplazaElLibre()
    {
        var doctorId = Guid.NewGuid();
        DoctorExists();

        var rule = TestData.MakeRule(doctorId, day: DayOfWeek.Tuesday);            // MARTES 09:00-12:00
        var booked = TestData.MakeSlot(doctorId, rule, start: new TimeOnly(10, 0), status: SlotStatus.Booked);
        var free = TestData.MakeSlot(doctorId, rule, start: new TimeOnly(9, 0));   // libre y futuro

        ExistingRules(rule);
        ExistingSlots(booked, free);
        _persistence.Setup(p => p.Add(It.IsAny<AvailabilityRule>())).ReturnsAsync((AvailabilityRule r) => r);
        _persistence.Setup(p => p.Add(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);
        _persistence.Setup(p => p.Delete(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);
        _persistence.Setup(p => p.Delete(It.IsAny<AvailabilityRule>())).ReturnsAsync((AvailabilityRule r) => r);

        // Reconfigura el mismo MARTES 09:00-12:00: no debe reventar por tener un turno reservado.
        var result = await _sut.Update(Req(doctorId, ("MARTES", "09:00", "12:00")));

        _persistence.Verify(p => p.Delete(booked), Times.Never);                   // reservado: intacto
        _persistence.Verify(p => p.Delete(free), Times.Once);                      // libre futuro: reemplazado
        _persistence.Verify(p => p.Delete(It.IsAny<AvailabilityRule>()), Times.Never); // regla con reserva: conservada
        _persistence.Verify(p => p.Add(It.IsAny<AvailabilityRule>()), Times.Never);    // regla idéntica: reutilizada
        Assert.Single(result.Days); // el schedule en efecto sigue siendo el MARTES 09-12 reutilizado
    }

    // ---------------------------------------------------------------- Mes destino (year / month)

    private static (int Year, int Month) MonthsFromNow(int months)
    {
        var date = DateTime.UtcNow.AddMonths(months);
        return (date.Year, date.Month);
    }

    private static AvailabilityModel.Request ReqForMonth(int? year, int? month) =>
        new(Guid.NewGuid(), [new AvailabilityModel.DayRequest("MARTES", "09:00", "12:00")], year, month);

    private (List<AvailabilityRule> Rules, List<AvailabilitySlot> Slots) CaptureCreated()
    {
        var rules = new List<AvailabilityRule>();
        var slots = new List<AvailabilitySlot>();
        _persistence.Setup(p => p.AddRange(It.IsAny<IEnumerable<AvailabilityRule>>()))
            .Callback<IEnumerable<AvailabilityRule>>(saved =>
            {
                rules.AddRange(saved);
                slots.AddRange(saved.SelectMany(r => r.Slots));
            })
            .Returns(Task.CompletedTask);
        return (rules, slots);
    }

    [Fact]
    public async Task Create_SinAnioNiMes_UsaElMesActual()
    {
        DoctorExists();
        var (rules, _) = CaptureCreated();
        var (year, month) = MonthsFromNow(0);

        var result = await _sut.Create(ReqForMonth(null, null));

        Assert.Equal((year, month), (result.Year, result.Month));
        Assert.Equal((year, month), ((int)rules.Single().Year, (int)rules.Single().Month));
    }

    [Fact]
    public async Task Create_ConMesSiguiente_GeneraLaReglaYLosTurnosDeEseMesDesdeElDia1()
    {
        DoctorExists();
        var (rules, slots) = CaptureCreated();
        var (year, month) = MonthsFromNow(1);

        var result = await _sut.Create(ReqForMonth(year, month));

        Assert.Equal((year, month), (result.Year, result.Month));
        Assert.Equal((year, month), ((int)rules.Single().Year, (int)rules.Single().Month));

        // Todos los martes del mes siguiente, desde el primero, con sus 6 turnos de 30 minutos.
        var tuesdays = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(day => new DateOnly(year, month, day))
            .Where(date => date.DayOfWeek == DayOfWeek.Tuesday)
            .ToList();
        Assert.Equal(tuesdays.Count * 6, slots.Count);
        Assert.Equal(tuesdays.First(), slots.Min(s => s.SlotDate));
    }

    [Fact]
    public async Task Update_ConMesSiguiente_ReconfiguraEseMes()
    {
        DoctorExists();
        ExistingRules();
        ExistingSlots();
        var (year, month) = MonthsFromNow(1);

        var result = await _sut.Update(ReqForMonth(year, month));

        Assert.Equal((year, month), (result.Year, result.Month));
    }

    [Theory]
    [InlineData(2026, null, "year_and_month_required_together")]
    [InlineData(null, 5, "year_and_month_required_together")]
    [InlineData(2026, 13, "month_between_1_and_12")]
    [InlineData(2026, 0, "month_between_1_and_12")]
    public async Task Create_AnioOMesInvalido_Lanza400(int? year, int? month, string issue)
    {
        DoctorExists();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(ReqForMonth(year, month)));

        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_MONTH_INVALID), ex.Error.ErrorCode);
        Assert.Equal(issue, ex.Error.Details.Single().Issue);
    }

    [Theory]
    [InlineData(-1, "month_in_the_past")]
    [InlineData(13, "month_max_12_ahead")]
    public async Task Create_MesPasadoOMuyLejano_Lanza400(int monthsFromNow, string issue)
    {
        DoctorExists();
        var (year, month) = MonthsFromNow(monthsFromNow);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(ReqForMonth(year, month)));

        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_MONTH_INVALID), ex.Error.ErrorCode);
        Assert.Equal(issue, ex.Error.Details.Single().Issue);
    }
}
