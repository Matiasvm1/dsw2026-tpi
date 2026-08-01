using Microsoft.Extensions.Configuration;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Helpers;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.Application.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly IPersistence _persistence;
    private readonly IConfiguration _configuration;

    public AvailabilityService(IPersistence persistence, IConfiguration configuration)
    {
        _persistence = persistence;
        _configuration = configuration;
    }

    public async Task<AvailabilityModel.Response> Create(AvailabilityModel.Request request)
    {
        await ValidateDoctorAndDays(request);

        var parsedDays = ParseAndValidatePayloadDays(request.Days);

        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;

        var existingRules = await _persistence.GetFiltered<AvailabilityRule>(
            r => r.DoctorId == request.DoctorId && r.Year == currentYear && r.Month == currentMonth);

        ValidateNoOverlapsWithExistingRules(parsedDays, existingRules);

        // Leer días no laborables desde appsettings
        var nonWorkingDays = GetNonWorkingDays();

        var rulesCreated = 0;
        var slotsCreated = 0;

        foreach (var dayReq in parsedDays)
        {
            var rule = new AvailabilityRule(request.DoctorId, (short)currentYear, (byte)currentMonth, dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
            // var rule = new AvailabilityRule(request.DoctorId, currentYear, currentMonth, dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
            await _persistence.Add(rule);
            rulesCreated++;

            var slots = GenerateSlots(rule, DateOnly.FromDateTime(now), now, nonWorkingDays);
            foreach (var slot in slots)
            {
                await _persistence.Add(slot);
                slotsCreated++;
            }
        }

        return new AvailabilityModel.Response(request.DoctorId, currentYear, currentMonth, rulesCreated, slotsCreated);
    }

    public async Task<AvailabilityModel.Response> Update(AvailabilityModel.Request request)
    {
        await ValidateDoctorAndDays(request);
        var parsedDays = ParseAndValidatePayloadDays(request.Days);

        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;

        var existingRules = await _persistence.GetFiltered<AvailabilityRule>(
            r => r.DoctorId == request.DoctorId && r.Year == currentYear && r.Month == currentMonth);

        if (existingRules.Any())
        {
            var ruleIds = existingRules.Select(r => r.Id).ToList();
            var existingSlots = await _persistence.GetFiltered<AvailabilitySlot>(s => ruleIds.Contains(s.AvailabilityRuleId));
            var slotIds = existingSlots.Select(s => s.Id).ToList();

            var hasBookedAppointments = await _persistence.First<Appointment>(
                a => slotIds.Contains(a.AvailabilitySlotId) && a.Status == AppointmentStatus.Booked);

            if (hasBookedAppointments is not null)
            {
                throw new ConflictException(
                    nameof(ErrorCodes.AVAILABILITY_HAS_BOOKED_APPOINTMENTS),
                    ErrorCodes.AVAILABILITY_HAS_BOOKED_APPOINTMENTS);
            }


            foreach (var rule in existingRules)
            {
                await _persistence.Delete(rule);
            }
        }

        var nonWorkingDays = GetNonWorkingDays();
        var rulesCreated = 0;
        var slotsCreated = 0;

        foreach (var dayReq in parsedDays)
        {
            var rule = new AvailabilityRule(request.DoctorId, (short)currentYear, (byte)currentMonth, dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
            // var rule = new AvailabilityRule(request.DoctorId, currentYear, currentMonth, dayReq.DayOfWeek, dayReq.StartTime, dayReq.EndTime);
            await _persistence.Add(rule);
            rulesCreated++;

            var slots = GenerateSlots(rule, DateOnly.FromDateTime(now), now, nonWorkingDays);
            foreach (var slot in slots)
            {
                await _persistence.Add(slot);
                slotsCreated++;
            }
        }

        return new AvailabilityModel.Response(request.DoctorId, currentYear, currentMonth, rulesCreated, slotsCreated);
    }

    #region Helpers & Validations

    private async Task ValidateDoctorAndDays(AvailabilityModel.Request request)
    {
        var doctor = await _persistence.GetById<Doctor>(request.DoctorId);
        if (doctor is null)
            throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND));

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

            if (!TimeOnly.TryParse(d.StartTime, out var startTime) || !TimeOnly.TryParse(d.EndTime, out var endTime))
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
        AvailabilityRule rule, DateOnly from, DateTime nowUtc, IReadOnlySet<DateOnly> nonWorkingDays)
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

                slots.Add(new AvailabilitySlot(rule.Id, rule.DoctorId, date, start, end));
            }
        }

        return slots;
    }

    private HashSet<DateOnly> GetNonWorkingDays()
    {
        var rawDays = _configuration.GetSection("NonWorkingDays").Get<string[]>();
        if (rawDays is null) return new HashSet<DateOnly>();

        return rawDays.Select(DateOnly.Parse).ToHashSet();
    }

    private record ParsedDayRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    #endregion
}