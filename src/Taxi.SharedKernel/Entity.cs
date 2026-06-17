namespace Taxi.SharedKernel;

/// <summary>
/// Classe de base des entités persistées : identité technique (<c>Id</c>) et date de création.
/// </summary>
public abstract class Entity
{
    public int Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
}
