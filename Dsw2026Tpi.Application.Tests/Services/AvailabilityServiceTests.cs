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

    private void DoctorExists() =>
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(TestData.MakeDoctor(Guid.NewGuid()));

    private void ExistingRules(params AvailabilityRule[] rules) =>
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<AvailabilityRule, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(rules);

    [Fact]
    public async Task Create_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
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
    public async Task Create_SeSolapaConReglaExistente_Lanza409()
    {
        var doctorId = Guid.NewGuid();
        DoctorExists();
        // Ya hay una regla MARTES 09:00-12:00 cargada para ese médico.
        ExistingRules(TestData.MakeRule(doctorId, day: DayOfWeek.Tuesday));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.Create(Req(doctorId, ("MARTES", "10:00", "11:00"))));
        Assert.Equal(nameof(ErrorCodes.AVAILABILITY_OVERLAP), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_ConDatosValidos_CreaLaReglaYDevuelveElResumen()
    {
        DoctorExists();
        ExistingRules(); // no hay reglas previas
        _persistence.Setup(p => p.Add(It.IsAny<AvailabilityRule>())).ReturnsAsync((AvailabilityRule r) => r);
        _persistence.Setup(p => p.Add(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);

        var result = await _sut.Create(Req(Guid.NewGuid(), ("MARTES", "09:00", "12:00")));

        Assert.Single(result.Days); // devuelve el schedule creado (1 día)
        _persistence.Verify(p => p.Add(It.IsAny<AvailabilityRule>()), Times.Once);
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
}
