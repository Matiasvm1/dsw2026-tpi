namespace Dsw2026Tpi.Domain.Entities;

public abstract class EntityBase(Guid? id = null)
{
    public Guid Id { get; init; } = id ?? Guid.NewGuid();

    public bool Deleted { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void SoftDelete()
    {
        Deleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
