using Microsoft.Extensions.Configuration;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Helpers;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly IPersistence _persistence;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(IPersistence persistence, IConfiguration configuration, ILogger<AvailabilityService> logger)
    {
        _persistence = persistence;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AvailabilityModel.Response> Create(AvailabilityModel.Request request)
    {
        await ValidateDoctorAndDays(request);

        var parsedDays = ParseAndValidatePayloadDays(request.Days);

        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;

        var existingRules = await _persistence.GetFiltered<AvailabilityRule>(
            r => r.DoctorId == request.DoctorId && r.Year == currentYear && r.Month == currentMonth) ?? [];

        ValidateNoOverlapsWithExistingRules(parsedDays, existingRules);

        // Leer días no laborables desde appsettings
        var nonWorkingDays = GetNonWorkingDays();

        var rulesCreated = 0;
        var slotsCreated = 0;
        var effectiveRules = new List<AvailabilityRule>();

        foreach (var dayReq in parsedDays)
        {
            var rule = new AvailabilityRule(request.DoctorId, (short)currentYear, (byte)currentMonth, dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
            await _persistence.Add(rule);
            rulesCreated++;
            effectiveRules.Add(rule);

            var slots = GenerateSlots(rule, DateOnly.FromDateTime(now), now, nonWorkingDays);
            foreach (var slot in slots)
            {
                await _persistence.Add(slot);
                slotsCreated++;
            }
        }

        // D08: la generación de disponibilidad no es un CRUD, se audita.
        _logger.LogInformation(
            "Disponibilidad generada para el médico {DoctorId} en {Month}/{Year}: {RulesCreated} reglas y {SlotsCreated} turnos",
            request.DoctorId, currentMonth, currentYear, rulesCreated, slotsCreated);

        return BuildResponse(request.DoctorId, currentYear, currentMonth, effectiveRules);
    }

    public async Task<AvailabilityModel.Response> Update(AvailabilityModel.Request request)
    {
        await ValidateDoctorAndDays(request);
        var parsedDays = ParseAndValidatePayloadDays(request.Days);

        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;

        var existingRules = (await _persistence.GetFiltered<AvailabilityRule>(
            r => r.DoctorId == request.DoctorId && r.Year == currentYear && r.Month == currentMonth) ?? []).ToList();

        var existingSlots = new List<AvailabilitySlot>();
        if (existingRules.Count > 0)
        {
            var ruleIds = existingRules.Select(r => r.Id).ToList();
            existingSlots = (await _persistence.GetFiltered<AvailabilitySlot>(
                s => ruleIds.Contains(s.AvailabilityRuleId)) ?? []).ToList();
        }

        // AVL-11: el PUT sobreescribe SOLO la disponibilidad NO reservada futura. Un slot reservado
        // (Booked) o pasado se conserva; los libres futuros se dan de baja para regenerarlos.
        var survivingSlots = new List<AvailabilitySlot>();
        foreach (var slot in existingSlots)
        {
            var isFuture = slot.SlotDate.ToDateTime(slot.StartTime) > now;
            if (slot.Status == SlotStatus.Booked || !isFuture)
                survivingSlots.Add(slot);
            else
                await _persistence.Delete(slot);
        }

        // Una regla se da de baja solo si no le queda ningún slot vivo. Si todavía sostiene un turno
        // reservado hay que conservarla: el filtro global de baja lógica la ocultaría y el grafo del
        // turno (slot -> regla -> médico) se rompería, dejando al paciente sin médico en su turno.
        var ruleIdsWithSurvivors = survivingSlots.Select(s => s.AvailabilityRuleId).ToHashSet();
        var keptRules = new List<AvailabilityRule>();
        foreach (var rule in existingRules)
        {
            if (ruleIdsWithSurvivors.Contains(rule.Id))
                keptRules.Add(rule);
            else
                await _persistence.Delete(rule);
        }

        // (fecha, hora) ya ocupadas por un slot vivo: la regeneración las saltea para no chocar con
        // el índice único (DoctorId, SlotDate, StartTime) filtrado por Deleted = 0.
        var occupied = survivingSlots.Select(s => (s.SlotDate, s.StartTime)).ToHashSet();

        var nonWorkingDays = GetNonWorkingDays();
        var rulesCreated = 0;
        var slotsCreated = 0;
        var effectiveRules = new List<AvailabilityRule>();

        foreach (var dayReq in parsedDays)
        {
            // Si una regla conservada coincide exactamente (día y horario) se reutiliza: crear otra
            // idéntica no borrada rompería el índice único de reglas.
            var rule = keptRules.FirstOrDefault(r =>
                r.DayOfWeek == dayReq.DayOfWeek &&
                r.StartTime == dayReq.StartTime &&
                r.EndTime == dayReq.EndTime);

            if (rule is null)
            {
                rule = new AvailabilityRule(request.DoctorId, (short)currentYear, (byte)currentMonth,
                    dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
                await _persistence.Add(rule);
                rulesCreated++;
            }

            effectiveRules.Add(rule);

            var slots = GenerateSlots(rule, DateOnly.FromDateTime(now), now, nonWorkingDays, occupied);
            foreach (var slot in slots)
            {
                await _persistence.Add(slot);
                slotsCreated++;
            }
        }

        // D08: la regeneración de disponibilidad tampoco es un CRUD, se audita.
        _logger.LogInformation(
            "Disponibilidad actualizada para el médico {DoctorId} en {Month}/{Year}: {RulesCreated} reglas nuevas y {SlotsCreated} turnos, conservando {Preserved} reservados/pasados",
            request.DoctorId, currentMonth, currentYear, rulesCreated, slotsCreated, survivingSlots.Count);

        return BuildResponse(request.DoctorId, currentYear, currentMonth, effectiveRules);
    }

    #region Helpers & Validations

    // Arma el response con el schedule en efecto (una fila por día, ordenado Lunes->Domingo),
    // en el mismo formato que GET /doctors/{id}/availabilities.
    private static AvailabilityModel.Response BuildResponse(
        Guid doctorId, int year, int month, IEnumerable<AvailabilityRule> rules)
    {
        var days = rules
            .OrderBy(r => r.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)r.DayOfWeek)
            .ThenBy(r => r.StartTime)
            .Select(r => new AvailabilityModel.DayResponse(
                DayOfWeekMapper.ToSpanish(r.DayOfWeek),
                r.StartTime.ToString("HH\\:mm"),
                r.EndTime.ToString("HH\\:mm")))
            .ToList();

        return new AvailabilityModel.Response(doctorId, year, month, days);
    }

    private async Task ValidateDoctorAndDays(AvailabilityModel.Request request)
    {
        var doctor = await _persistence.GetById<Doctor>(request.DoctorId);
        if (doctor is null)
            throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ErrorCodes.DOCTOR_NOT_FOUND);

        if (request.Days is null || !request.Days.Any())
            throw new ValidationException(nameof(ErrorCodes.AVAILABILITY_DAYS_REQUIRED), ErrorCodes.AVAILABILITY_DAYS_REQUIRED);
    }

    private static List<ParsedDayRequest> ParseAndValidatePayloadDays(IReadOnlyList<AvailabilityModel.DayRequest> days)
    {
        var result = new List<ParsedDayRequest>();

        foreach (var d in days)
        {
            if (!DayOfWeekMapper.TryParse(d.Day, out var dayOfWeek))
                throw new ValidationException(nameof(ErrorCodes.AVAILABILITY_INVALID_DAY), ErrorCodes.AVAILABILITY_INVALID_DAY);

            // El contrato pide "HH:mm" y nada más. TryParse a secas acepta "9", "9:00 AM" o
            // "09:00:45", y los segundos se escaparían del control de alineación de más abajo.
            if (!TimeOnly.TryParseExact(d.StartTime, "HH:mm", out var startTime) ||
                !TimeOnly.TryParseExact(d.EndTime, "HH:mm", out var endTime))
                throw new ValidationException(nameof(ErrorCodes.AVAILABILITY_INVALID_TIME), ErrorCodes.AVAILABILITY_INVALID_TIME);

            if (startTime >= endTime)
                throw new ValidationException(nameof(ErrorCodes.AVAILABILITY_INVALID_RANGE), ErrorCodes.AVAILABILITY_INVALID_RANGE);

            if (startTime.Minute % 30 != 0 || endTime.Minute % 30 != 0)
                throw new ValidationException(nameof(ErrorCodes.AVAILABILITY_NOT_ALIGNED), ErrorCodes.AVAILABILITY_NOT_ALIGNED);

            result.Add(new ParsedDayRequest(dayOfWeek, startTime, endTime));
        }

        for (int i = 0; i < result.Count; i++)
        {
            for (int j = i + 1; j < result.Count; j++)
            {
                if (result[i].DayOfWeek == result[j].DayOfWeek &&
                    result[i].StartTime < result[j].EndTime &&
                    result[j].StartTime < result[i].EndTime)
                {
                    throw new ConflictException(nameof(ErrorCodes.AVAILABILITY_OVERLAP), ErrorCodes.AVAILABILITY_OVERLAP);
                }
            }
        }

        return result;
    }

    private static void ValidateNoOverlapsWithExistingRules(List<ParsedDayRequest> newDays, IEnumerable<AvailabilityRule> existingRules)
    {
        foreach (var newDay in newDays)
        {
            foreach (var existing in existingRules)
            {
                if (newDay.DayOfWeek == existing.DayOfWeek &&
                    newDay.StartTime < existing.EndTime &&
                    existing.StartTime < newDay.EndTime)
                {
                    throw new ConflictException(nameof(ErrorCodes.AVAILABILITY_OVERLAP), ErrorCodes.AVAILABILITY_OVERLAP);
                }
            }
        }
    }

    private static IEnumerable<AvailabilitySlot> GenerateSlots(
        AvailabilityRule rule, DateOnly from, DateTime nowUtc, IReadOnlySet<DateOnly> nonWorkingDays,
        IReadOnlySet<(DateOnly Date, TimeOnly Start)>? occupied = null)
    {
        var lastDay = DateTime.DaysInMonth(rule.Year, rule.Month);
        var slots = new List<AvailabilitySlot>();

        for (var day = from.Day; day <= lastDay; day++)
        {
            var date = new DateOnly(rule.Year, rule.Month, day);
            if (date.DayOfWeek != rule.DayOfWeek) continue;

            if (nonWorkingDays.Contains(date)) continue;

            for (var start = rule.StartTime; start < rule.EndTime; start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(30);

                if (date.ToDateTime(start) <= nowUtc) continue;

                // No regenerar un horario que ya sostiene un slot vivo (reservado o pasado).
                if (occupied is not null && occupied.Contains((date, start))) continue;

                slots.Add(new AvailabilitySlot(rule.Id, rule.DoctorId, date, start, end));
            }
        }

        return slots;
    }

    private HashSet<DateOnly> GetNonWorkingDays()
    {
        var rawDays = _configuration.GetSection("NonWorkingDays").Get<string[]>();
        if (rawDays is null) return [];

        var days = new HashSet<DateOnly>();

        foreach (var raw in rawDays)
        {
            // Una fecha mal escrita en el appsettings no puede tumbar el alta de disponibilidad
            // con un 500: se descarta y queda registrada para que se corrija la configuración.
            if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date))
            {
                days.Add(date);
            }
            else
            {
                _logger.LogWarning("Fecha inválida en NonWorkingDays, se ignora: {RawDate}", raw);
            }
        }

        return days;
    }

    private record ParsedDayRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    #endregion
}