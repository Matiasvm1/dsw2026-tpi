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
/// Tests del CRUD de médicos: validación de nombre, existencia de la especialidad y no-encontrado.
/// </summary>
public class DoctorServiceTests
{
    private readonly Mock<IPersistence> _persistence = new();
    private readonly DoctorService _sut;

    public DoctorServiceTests()
    {
        _sut = new DoctorService(_persistence.Object);
    }

    private static DoctorModel.Request Req(Guid specialityId, string name = "Dr House") =>
        new(name, "MP12345", specialityId);

    // ---------------------------------------------------------------- Create

    [Fact]
    public async Task Create_NombreMuyCorto_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(Req(Guid.NewGuid(), "AB")));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NAME_LENGTH), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_EspecialidadInexistente_Lanza400()
    {
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Speciality?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(Req(Guid.NewGuid())));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_SPECIALTY_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_ConDatosValidos_CreaYDevuelveElMedico()
    {
        var spec = TestData.MakeSpeciality();
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(spec);
        _persistence.Setup(p => p.Add(It.IsAny<Doctor>())).ReturnsAsync((Doctor d) => d);

        var result = await _sut.Create(Req(spec.Id));

        Assert.Equal("Dr House", result.Name);
        Assert.Equal(spec.Id, result.Specialty!.Id);
        _persistence.Verify(p => p.Add(It.IsAny<Doctor>()), Times.Once);
    }

    // ---------------------------------------------------------------- Update

    [Fact]
    public async Task Update_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Doctor?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Update(Guid.NewGuid(), Req(Guid.NewGuid())));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Update_ConDatosValidos_ActualizaElMedico()
    {
        var spec = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(spec.Id);
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(doctor);
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(spec);
        _persistence.Setup(p => p.Update(It.IsAny<Doctor>())).ReturnsAsync((Doctor d) => d);

        var result = await _sut.Update(doctor.Id, Req(spec.Id, "Dr Wilson"));

        Assert.Equal("Dr Wilson", result.Name);
        _persistence.Verify(p => p.Update(It.IsAny<Doctor>()), Times.Once);
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public async Task Delete_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Doctor?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Delete(Guid.NewGuid()));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Delete_MedicoExistente_LlamaDelete()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(doctor);
        _persistence.Setup(p => p.Delete(It.IsAny<Doctor>())).ReturnsAsync((Doctor d) => d);

        await _sut.Delete(doctor.Id);

        _persistence.Verify(p => p.Delete(It.IsAny<Doctor>()), Times.Once);
    }

    // ---------------------------------------------------------------- GetAll / GetAvailabilities

    [Fact]
    public async Task GetAll_ConFiltroDeNombreMuyCorto_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.GetAll(new PaginationQuery(null, null), "AB"));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NAME_LENGTH), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetAvailabilities_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Doctor?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.GetAvailabilities(Guid.NewGuid()));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    // ---------------------------------------------------------------- GetSlots

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private void SetupExistingDoctor(Doctor doctor) =>
        _persistence.Setup(p => p.GetById<Doctor>(doctor.Id, It.IsAny<string[]>())).ReturnsAsync(doctor);

    // Devuelve los slots indicados y guarda el predicado de GetFiltered para poder evaluarlo.
    private Func<AvailabilitySlot, bool> SetupSlots(params AvailabilitySlot[] slots)
    {
        Expression<Func<AvailabilitySlot, bool>>? captured = null;
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<AvailabilitySlot, bool>>>(), It.IsAny<string[]>()))
            .Callback<Expression<Func<AvailabilitySlot, bool>>, string[]>((predicate, _) => captured = predicate)
            .ReturnsAsync(slots);

        return slot => captured!.Compile()(slot);
    }

    [Fact]
    public async Task GetSlots_MedicoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Doctor?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.GetSlots(Guid.NewGuid(), null, null));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Theory]
    [InlineData("10/08/2026", null)]
    [InlineData(null, "2026-13-01")]
    public async Task GetSlots_FechaConFormatoInvalido_Lanza400(string? from, string? to)
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        SetupExistingDoctor(doctor);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.GetSlots(doctor.Id, from, to));
        Assert.Equal(nameof(ErrorCodes.SLOT_RANGE_INVALID), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetSlots_HastaAnteriorADesde_Lanza400()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        SetupExistingDoctor(doctor);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.GetSlots(doctor.Id, "2026-08-10", "2026-08-09"));
        Assert.Equal(nameof(ErrorCodes.SLOT_RANGE_INVALID), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetSlots_RangoMayorA31Dias_Lanza400()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        SetupExistingDoctor(doctor);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.GetSlots(doctor.Id, "2026-08-01", "2026-09-01"));
        Assert.Equal(nameof(ErrorCodes.SLOT_RANGE_INVALID), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task GetSlots_SinFechas_ConsultaDesdeHoyLosProximos7Dias()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        SetupExistingDoctor(doctor);
        var predicate = SetupSlots();

        await _sut.GetSlots(doctor.Id, null, null);

        Assert.True(predicate(TestData.MakeSlot(doctor.Id, date: Today)));
        Assert.True(predicate(TestData.MakeSlot(doctor.Id, date: Today.AddDays(6))));
        Assert.False(predicate(TestData.MakeSlot(doctor.Id, date: Today.AddDays(7))));
        Assert.False(predicate(TestData.MakeSlot(doctor.Id, date: Today.AddDays(-1))));
        Assert.False(predicate(TestData.MakeSlot(Guid.NewGuid(), date: Today)));
    }

    [Fact]
    public async Task GetSlots_DevuelveSoloFuturosOrdenadosYConSuEstado()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        SetupExistingDoctor(doctor);
        var tomorrow = Today.AddDays(1);
        var past = TestData.MakeSlot(doctor.Id, date: Today.AddDays(-1));
        var booked = TestData.MakeSlot(doctor.Id, date: tomorrow, start: new TimeOnly(9, 30), status: SlotStatus.Booked);
        var available = TestData.MakeSlot(doctor.Id, date: tomorrow, start: new TimeOnly(9, 0));
        SetupSlots(booked, past, available);

        var result = (await _sut.GetSlots(doctor.Id, null, null)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(available.Id, result[0].Id);
        Assert.Equal(tomorrow.ToString("yyyy-MM-dd"), result[0].Date);
        Assert.Equal("09:00", result[0].StartTime);
        Assert.Equal("09:30", result[0].EndTime);
        Assert.Equal("AVAILABLE", result[0].Status);
        Assert.Equal(booked.Id, result[1].Id);
        Assert.Equal("BOOKED", result[1].Status);
    }

    [Fact]
    public async Task GetAvailabilities_DevuelveLasReglasEnEspañol()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(doctor);
        _persistence.Setup(p => p.GetFiltered(
                It.IsAny<Expression<Func<AvailabilityRule, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new[] { TestData.MakeRule(doctor.Id, day: DayOfWeek.Tuesday) });

        var result = (await _sut.GetAvailabilities(doctor.Id)).ToList();

        Assert.Single(result);
        Assert.Equal("MARTES", result[0].Day);
        Assert.Equal("09:00", result[0].StartTime);
    }
}
