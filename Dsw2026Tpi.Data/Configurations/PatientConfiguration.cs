using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).IsRequired().HasMaxLength(450);
        builder.Property(p => p.Dni).IsRequired().HasMaxLength(10);
        builder.Property(p => p.FullName).HasMaxLength(150);   // varchar(150) y NULL según el modelo de datos (D35)

        builder.HasIndex(p => p.Dni).IsUnique();
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}