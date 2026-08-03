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
/// Tests del CRUD de especialidades: longitudes de nombre/descripción, unicidad del nombre
/// (409 antes de reventar contra el índice único) y no-encontrado.
/// </summary>
public class SpecialityServiceTests
{
    private readonly Mock<IPersistence> _persistence = new();
    private readonly SpecialityService _sut;

    public SpecialityServiceTests()
    {
        _sut = new SpecialityService(_persistence.Object);
    }

    private static SpecialityModel.Request Req(string name = "Cardiologia",
        string description = "Especialidad del corazon") => new(name, description);

    private void SetupFirst(Speciality? result) =>
        _persistence.Setup(p => p.First(It.IsAny<Expression<Func<Speciality, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(result);

    // ---------------------------------------------------------------- Create

    [Fact]
    public async Task Create_NombreMuyCorto_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(Req(name: "AB")));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_NAME_LENGTH), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_DescripcionMuyCorta_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Create(Req(description: "corta")));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_DESCRIPTION_LENGTH), ex.Error.ErrorCode);
    }

    // La verificación previa devuelve 409 limpio en vez de dejar reventar el índice único con 500.
    [Fact]
    public async Task Create_NombreDuplicado_Lanza409()
    {
        SetupFirst(TestData.MakeSpeciality());

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.Create(Req()));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Create_ConDatosValidos_CreaYDevuelve()
    {
        SetupFirst(null);
        _persistence.Setup(p => p.Add(It.IsAny<Speciality>())).ReturnsAsync((Speciality s) => s);

        var result = await _sut.Create(Req());

        Assert.Equal("Cardiologia", result.Name);
        _persistence.Verify(p => p.Add(It.IsAny<Speciality>()), Times.Once);
    }

    // ---------------------------------------------------------------- Update

    [Fact]
    public async Task Update_Inexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Speciality?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Update(Guid.NewGuid(), Req()));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Update_NombreDuplicadoEnOtra_Lanza409()
    {
        var actual = TestData.MakeSpeciality();
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(actual);
        SetupFirst(TestData.MakeSpeciality("Otra")); // ya existe otra con ese nombre

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.Update(actual.Id, Req()));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Update_ConDatosValidos_Actualiza()
    {
        var actual = TestData.MakeSpeciality();
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(actual);
        SetupFirst(null);
        _persistence.Setup(p => p.Update(It.IsAny<Speciality>())).ReturnsAsync((Speciality s) => s);

        var result = await _sut.Update(actual.Id, Req("Neurologia", "Sistema nervioso central"));

        Assert.Equal("Neurologia", result.Name);
        _persistence.Verify(p => p.Update(It.IsAny<Speciality>()), Times.Once);
    }

    // ---------------------------------------------------------------- Delete / GetAll

    [Fact]
    public async Task Delete_Inexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Speciality?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Delete(Guid.NewGuid()));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Delete_Existente_LlamaDelete()
    {
        var spec = TestData.MakeSpeciality();
        _persistence.Setup(p => p.GetById<Speciality>(It.IsAny<Guid>(), It.IsAny<string[]>())).ReturnsAsync(spec);
        _persistence.Setup(p => p.Delete(It.IsAny<Speciality>())).ReturnsAsync((Speciality s) => s);

        await _sut.Delete(spec.Id);

        _persistence.Verify(p => p.Delete(It.IsAny<Speciality>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_FiltroDeNombreMuyCorto_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.GetAll(new PaginationQuery(null, null), "AB"));
        Assert.Equal(nameof(ErrorCodes.SPECIALTY_NAME_LENGTH), ex.Error.ErrorCode);
    }
}
