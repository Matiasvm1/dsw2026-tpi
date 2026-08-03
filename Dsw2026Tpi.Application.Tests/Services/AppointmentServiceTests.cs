using System.Linq.Expressions;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.Application.Tests.Support;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dsw2026Tpi.Application.Tests.Services;

/// <summary>
/// Tests unitarios del corazón del TFI: la reserva y la cancelación de turnos. Se mockea
/// IPersistence (no hay base ni HTTP) y se prueba SOLO la lógica de negocio del service.
/// Cada test documenta una regla: RN03 (concurrencia), RN04 (futuro), D20 (alcance del paciente).
/// </summary>
public class AppointmentServiceTests
{
    private readonly Mock<IPersistence> _persistence = new();
    private readonly AppointmentService _sut;

    public AppointmentServiceTests()
    {
        _sut = new AppointmentService(_persistence.Object, Mock.Of<ILogger<AppointmentService>>());
    }

    private static AppointmentModel.Request Req(Guid doctorId, Guid slotId,
        long dni = 12345678, string reason = "consulta general")
        => new(doctorId, slotId, new AppointmentModel.PatientRequest(dni), reason);

    // Cablea el camino feliz completo; cada test sobreescribe lo que necesita romper.
    private (Patient patient, Doctor doctor, AvailabilitySlot slot) ArrangeHappyPath()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var slot = TestData.MakeSlot(doctor.Id); // disponible y futuro por defecto

        _persistence.Setup(p => p.First(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(patient);
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(doctor);
        _persistence.Setup(p => p.GetById<AvailabilitySlot>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(slot);
        _persistence.Setup(p => p.Add(It.IsAny<Appointment>())).ReturnsAsync((Appointment a) => a);
        _persistence.Setup(p => p.Update(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);

        return (patient, doctor, slot);
    }

    // ---------------------------------------------------------------- Book: validaciones

    [Fact]
    public async Task Book_ConReasonMenorA5Caracteres_LanzaValidationException()
    {
        var user = Principals.Patient("12345678");
        var req = Req(Guid.NewGuid(), Guid.NewGuid(), reason: "cor");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_REASON_LENGTH), ex.Error.ErrorCode);
    }

    [Theory]
    [InlineData(123456)]      // 6 dígitos
    [InlineData(12345678901)] // 11 dígitos
    [InlineData(0)]           // no positivo
    public async Task Book_ConDniFueraDeRango_LanzaValidationException(long dni)
    {
        var user = Principals.Patient(dni.ToString());
        var req = Req(Guid.NewGuid(), Guid.NewGuid(), dni: dni);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.PATIENT_DNI_INVALID), ex.Error.ErrorCode);
    }

    // D20: el paciente solo reserva con SU propio DNI. El del body debe coincidir con el del token.
    [Fact]
    public async Task Book_PacienteConDniDistintoAlDelToken_Lanza403()
    {
        var user = Principals.Patient("12345678");
        var req = Req(Guid.NewGuid(), Guid.NewGuid(), dni: 87654321);

        var ex = await Assert.ThrowsAsync<AuthorizationException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_FORBIDDEN), ex.Error.ErrorCode);
    }

    // ---------------------------------------------------------------- Book: no encontrado

    [Fact]
    public async Task Book_PacienteInexistente_Lanza404()
    {
        _persistence.Setup(p => p.First(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync((Patient?)null);
        var user = Principals.Patient("12345678");
        var req = Req(Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.PATIENT_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Book_MedicoInexistente_Lanza404()
    {
        ArrangeHappyPath();
        _persistence.Setup(p => p.GetById<Doctor>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Doctor?)null);
        var user = Principals.Patient("12345678");
        var req = Req(Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Book_SlotInexistente_Lanza404()
    {
        ArrangeHappyPath();
        _persistence.Setup(p => p.GetById<AvailabilitySlot>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((AvailabilitySlot?)null);
        var user = Principals.Patient("12345678");
        var req = Req(Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_SLOT_NOT_FOUND), ex.Error.ErrorCode);
    }

    // ---------------------------------------------------------------- Book: reglas de negocio

    [Fact]
    public async Task Book_SlotDeOtroMedico_Lanza400DoctorMismatch()
    {
        var (_, doctor, _) = ArrangeHappyPath();
        // Un slot cuyo DoctorId NO es el del médico consultado.
        var slotDeOtro = TestData.MakeSlot(Guid.NewGuid());
        _persistence.Setup(p => p.GetById<AvailabilitySlot>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(slotDeOtro);
        var user = Principals.Patient("12345678");
        var req = Req(doctor.Id, slotDeOtro.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_DOCTOR_MISMATCH), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Book_SlotNoDisponible_Lanza409()
    {
        var (_, doctor, _) = ArrangeHappyPath();
        var slotOcupado = TestData.MakeSlot(doctor.Id, status: SlotStatus.Booked);
        _persistence.Setup(p => p.GetById<AvailabilitySlot>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(slotOcupado);
        var user = Principals.Patient("12345678");
        var req = Req(doctor.Id, slotOcupado.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_SLOT_UNAVAILABLE), ex.Error.ErrorCode);
    }

    // RN04: no se puede reservar un turno en el pasado.
    [Fact]
    public async Task Book_SlotEnElPasado_Lanza400InThePast()
    {
        var (_, doctor, _) = ArrangeHappyPath();
        var slotPasado = TestData.MakeSlot(doctor.Id,
            date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        _persistence.Setup(p => p.GetById<AvailabilitySlot>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(slotPasado);
        var user = Principals.Patient("12345678");
        var req = Req(doctor.Id, slotPasado.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_IN_THE_PAST), ex.Error.ErrorCode);
    }

    // RN03: la carrera real la resuelve el índice único. Cuando el INSERT choca, PersistenceEf
    // traduce a ConflictException y el service la reempaqueta como 409 APPOINTMENT_CONFLICT.
    [Fact]
    public async Task Book_CuandoElIndiceRechazaLaInsercion_Lanza409Conflict()
    {
        var (_, doctor, slot) = ArrangeHappyPath();
        _persistence.Setup(p => p.Add(It.IsAny<Appointment>()))
            .ThrowsAsync(new ConflictException(
                nameof(ErrorCodes.UNIQUE_CONSTRAINT_VIOLATION), ErrorCodes.UNIQUE_CONSTRAINT_VIOLATION));
        var user = Principals.Patient("12345678");
        var req = Req(doctor.Id, slot.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Book(req, user));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_CONFLICT), ex.Error.ErrorCode);
    }

    // ---------------------------------------------------------------- Book: camino feliz

    [Fact]
    public async Task Book_ConDatosValidos_DevuelveResponseCompletoYReservaElSlot()
    {
        var (patient, doctor, slot) = ArrangeHappyPath();
        var user = Principals.Patient("12345678", patient.Id);
        var req = Req(doctor.Id, slot.Id);

        var result = await _sut.Book(req, user);

        Assert.Equal("BOOKED", result.Status);
        Assert.Equal(doctor.Name, result.Doctor.Name);
        Assert.Equal("Cardiologia", result.Specialty.Name);
        Assert.Equal(patient.Dni, result.Patient.Dni);
        // D18: tras el INSERT exitoso, el slot queda BOOKED.
        Assert.Equal(SlotStatus.Booked, slot.Status);
        _persistence.Verify(p => p.Add(It.IsAny<Appointment>()), Times.Once);
        _persistence.Verify(p => p.Update(It.IsAny<AvailabilitySlot>()), Times.Once);
    }

    // El administrador puede reservar en nombre de cualquier paciente (sin el candado de D20).
    [Fact]
    public async Task Book_ComoAdmin_ConDniDeOtroPaciente_Reserva()
    {
        var (_, doctor, slot) = ArrangeHappyPath();
        var user = Principals.Admin();
        var req = Req(doctor.Id, slot.Id, dni: 99999999);

        var result = await _sut.Book(req, user);

        Assert.Equal("BOOKED", result.Status);
    }

    // ---------------------------------------------------------------- Cancel

    [Fact]
    public async Task Cancel_TurnoInexistente_Lanza404()
    {
        _persistence.Setup(p => p.GetById<Appointment>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync((Appointment?)null);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Cancel(Guid.NewGuid(), Principals.Patient("12345678")));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_NOT_FOUND), ex.Error.ErrorCode);
    }

    // REGRESIÓN del bug corregido: un paciente NO puede cancelar el turno de otro. Antes devolvía
    // 204 porque comparaba el rol contra la constante equivocada. Ahora tiene que ser 403.
    [Fact]
    public async Task Cancel_PacienteCancelaTurnoAjeno_Lanza403()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var dueño = TestData.MakePatient("87654321");
        var turnoAjeno = TestData.MakeBookedAppointment(dueño, doctor, speciality);
        _persistence.Setup(p => p.GetById<Appointment>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(turnoAjeno);

        // Otro paciente (patientId distinto al dueño del turno) intenta cancelarlo.
        var intruso = Principals.Patient("12345678", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<AuthorizationException>(
            () => _sut.Cancel(turnoAjeno.Id, intruso));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_FORBIDDEN), ex.Error.ErrorCode);
    }

    [Fact]
    public async Task Cancel_TurnoNoBooked_Lanza409NotCancellable()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var turno = TestData.MakeBookedAppointment(patient, doctor, speciality);
        turno.Cancel(); // ya cancelado -> no es BOOKED
        _persistence.Setup(p => p.GetById<Appointment>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(turno);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Cancel(turno.Id, Principals.Patient("12345678", patient.Id)));
        Assert.Equal(nameof(ErrorCodes.APPOINTMENT_NOT_CANCELLABLE), ex.Error.ErrorCode);
    }

    // Camino feliz: el dueño cancela. El turno queda CANCELLED y el slot vuelve a AVAILABLE (CU03).
    [Fact]
    public async Task Cancel_PropioYBooked_CancelaYLiberaElSlot()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("12345678");
        var turno = TestData.MakeBookedAppointment(patient, doctor, speciality);
        _persistence.Setup(p => p.GetById<Appointment>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(turno);
        _persistence.Setup(p => p.Update(It.IsAny<Appointment>())).ReturnsAsync((Appointment a) => a);
        _persistence.Setup(p => p.Update(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);

        await _sut.Cancel(turno.Id, Principals.Patient("12345678", patient.Id));

        Assert.Equal(AppointmentStatus.Cancelled, turno.Status);
        Assert.Equal(SlotStatus.Available, turno.AvailabilitySlot!.Status);
        _persistence.Verify(p => p.Update(It.IsAny<Appointment>()), Times.Once);
        _persistence.Verify(p => p.Update(It.IsAny<AvailabilitySlot>()), Times.Once);
    }

    // El admin puede cancelar el turno de cualquiera (no aplica el candado de D20).
    [Fact]
    public async Task Cancel_ComoAdmin_CancelaTurnoDeCualquiera()
    {
        var speciality = TestData.MakeSpeciality();
        var doctor = TestData.MakeDoctor(speciality.Id, speciality);
        var patient = TestData.MakePatient("87654321");
        var turno = TestData.MakeBookedAppointment(patient, doctor, speciality);
        _persistence.Setup(p => p.GetById<Appointment>(It.IsAny<Guid>(), It.IsAny<string[]>()))
            .ReturnsAsync(turno);
        _persistence.Setup(p => p.Update(It.IsAny<Appointment>())).ReturnsAsync((Appointment a) => a);
        _persistence.Setup(p => p.Update(It.IsAny<AvailabilitySlot>())).ReturnsAsync((AvailabilitySlot s) => s);

        await _sut.Cancel(turno.Id, Principals.Admin());

        Assert.Equal(AppointmentStatus.Cancelled, turno.Status);
    }
}
