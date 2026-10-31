using Dsw2026Tpi.Application.Tests.Support;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Tests.Domain;

/// <summary>
/// GRASP Expert and Creator: the doctor knows and creates its availability rules, and each rule
/// creates its slots.
/// </summary>
public class AvailabilityDomainTests
{
    private const short Year = 2030;
    private const byte Month = 1;
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Ten = new(10, 0);
    private static readonly TimeOnly Eleven = new(11, 0);
    private static readonly TimeOnly Noon = new(12, 0);
    private static readonly DateTime Past = new(2029, 12, 1);
    private static readonly DateOnly MonthStart = new(Year, Month, 1);

    private static Doctor DoctorWithTuesdayMorning()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());
        doctor.AddAvailability(Year, Month, DayOfWeek.Tuesday, Nine, Eleven);
        return doctor;
    }

    [Fact]
    public void AddAvailability_CreatesTheRuleInsideTheDoctor()
    {
        var doctor = TestData.MakeDoctor(Guid.NewGuid());

        var rule = doctor.AddAvailability(Year, Month, DayOfWeek.Tuesday, Nine, Eleven);

        Assert.Same(rule, Assert.Single(doctor.AvailabilityRules));
        Assert.Equal(doctor.Id, rule.DoctorId);
        Assert.Equal((Year, Month, DayOfWeek.Tuesday, Nine, Eleven),
            (rule.Year, rule.Month, rule.DayOfWeek, rule.StartTime, rule.EndTime));
    }

    [Fact]
    public void CheckAvailability_OverlappingRange_ReturnsFalse() =>
        Assert.False(DoctorWithTuesdayMorning().CheckAvailability(Year, Month, DayOfWeek.Tuesday, Ten, Noon));

    [Fact]
    public void CheckAvailability_AdjacentRange_ReturnsTrue() =>
        Assert.True(DoctorWithTuesdayMorning().CheckAvailability(Year, Month, DayOfWeek.Tuesday, Eleven, Noon));

    [Fact]
    public void CheckAvailability_OtherDay_ReturnsTrue() =>
        Assert.True(DoctorWithTuesdayMorning().CheckAvailability(Year, Month, DayOfWeek.Monday, Nine, Eleven));

    [Fact]
    public void CheckAvailability_OtherMonth_ReturnsTrue() =>
        Assert.True(DoctorWithTuesdayMorning().CheckAvailability(Year, 2, DayOfWeek.Tuesday, Nine, Eleven));

    [Fact]
    public void CheckAvailability_DeletedRule_IsIgnored()
    {
        var doctor = DoctorWithTuesdayMorning();
        doctor.AvailabilityRules.Single().SoftDelete();

        Assert.True(doctor.CheckAvailability(Year, Month, DayOfWeek.Tuesday, Nine, Eleven));
    }

    [Fact]
    public void GenerateSlots_CreatesThe30MinuteSlotsOfEveryMatchingDayInsideTheRule()
    {
        var rule = DoctorWithTuesdayMorning().AvailabilityRules.Single();

        var slots = rule.GenerateSlots(MonthStart, Past, new HashSet<DateOnly>());

        // January 2030 has 5 Tuesdays, each one with 4 slots between 09:00 and 11:00.
        Assert.Equal(20, slots.Count);
        Assert.Equal(20, rule.Slots.Count);
        Assert.All(slots, s =>
        {
            Assert.Equal(DayOfWeek.Tuesday, s.SlotDate.DayOfWeek);
            Assert.Equal(rule.Id, s.AvailabilityRuleId);
            Assert.Equal(rule.DoctorId, s.DoctorId);
            Assert.Equal(s.StartTime.AddMinutes(30), s.EndTime);
            Assert.Equal(SlotStatus.Available, s.Status);
        });
    }

    [Fact]
    public void GenerateSlots_SkipsNonWorkingDaysPastTimesAndOccupiedSlots()
    {
        var rule = DoctorWithTuesdayMorning().AvailabilityRules.Single();
        var firstTuesday = new DateOnly(2030, 1, 1);
        var secondTuesday = new DateOnly(2030, 1, 8);

        var slots = rule.GenerateSlots(
            MonthStart,
            new DateTime(2030, 1, 1, 0, 0, 0),
            new HashSet<DateOnly> { firstTuesday },
            new HashSet<(DateOnly, TimeOnly)> { (secondTuesday, Nine) });

        Assert.DoesNotContain(slots, s => s.SlotDate == firstTuesday);
        Assert.DoesNotContain(slots, s => s.SlotDate == secondTuesday && s.StartTime == Nine);
        Assert.Equal(15, slots.Count); // 3 on the 8th and 4 on each of the 15th, 22nd and 29th
    }

    [Fact]
    public void GenerateSlots_SkipsSlotsThatAlreadyStarted()
    {
        var rule = DoctorWithTuesdayMorning().AvailabilityRules.Single();

        var slots = rule.GenerateSlots(MonthStart, new DateTime(2030, 1, 29, 9, 0, 0), new HashSet<DateOnly>());

        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal(new DateOnly(2030, 1, 29), s.SlotDate));
    }
}
