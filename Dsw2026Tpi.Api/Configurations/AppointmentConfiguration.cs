using System;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Reason).IsRequired().HasMaxLength(300);

        builder.Property(a => a.Status)
               .IsRequired()
               .HasMaxLength(20)
               .HasConversion(
                   v => v == AppointmentStatus.NoShow ? "NO_SHOW" : v.ToString().ToUpperInvariant(),
                   v => v == "NO_SHOW" ? AppointmentStatus.NoShow : Enum.Parse<AppointmentStatus>(v, true));

        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasOne(a => a.AvailabilitySlot)
               .WithMany()
               .HasForeignKey(a => a.AvailabilitySlotId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Patient)
               .WithMany()
               .HasForeignKey(a => a.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // RN03 — un slot no puede tener más de un turno ACTIVO.
        // Índice filtrado: los turnos cancelados no bloquean que el slot se reserve de nuevo.
        builder.HasIndex(a => a.AvailabilitySlotId)
               .IsUnique()
               .HasFilter("[Status] = 'BOOKED' AND [Deleted] = 0");
    }
}