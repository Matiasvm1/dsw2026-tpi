using System;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("AvailabilitySlots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SlotDate).IsRequired();
        builder.Property(s => s.StartTime).IsRequired();
        builder.Property(s => s.EndTime).IsRequired();

        builder.Property(s => s.Status)
               .IsRequired()
               .HasMaxLength(20)
               .HasConversion(
                   v => v.ToString().ToUpperInvariant(),
                   v => Enum.Parse<SlotStatus>(v, true));

        builder.HasOne(s => s.AvailabilityRule)
               .WithMany(r => r.Slots)
               .HasForeignKey(s => s.AvailabilityRuleId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.DoctorId, s.SlotDate, s.StartTime }).IsUnique();
    }
}