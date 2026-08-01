using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("AvailabilityRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Year).IsRequired();
        builder.Property(r => r.Month).IsRequired();
        builder.Property(r => r.DayOfWeek).IsRequired().HasConversion<byte>();
        builder.Property(r => r.StartTime).IsRequired();
        builder.Property(r => r.EndTime).IsRequired();

        builder.HasOne(r => r.Doctor)
               .WithMany()
               .HasForeignKey(r => r.DoctorId)
               .OnDelete(DeleteBehavior.Restrict);

        // Filtrado por Deleted: el PUT de disponibilidades da de baja las reglas del mes y las
        // vuelve a generar. Sin el filtro, reconfigurar el mismo día con el mismo horario choca
        // contra la regla vieja y devuelve un 500.
        builder.HasIndex(r => new { r.DoctorId, r.Year, r.Month, r.DayOfWeek, r.StartTime, r.EndTime })
               .IsUnique()
               .HasFilter("[Deleted] = 0");
    }
}