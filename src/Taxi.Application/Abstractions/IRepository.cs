using Ardalis.Specification;
using Taxi.SharedKernel;

namespace Taxi.Application.Abstractions;

/// <summary>
/// Dépôt générique (Repository + Specification d'Ardalis) pour une entité du domaine. Abstrait l'accès aux données : la couche Application ne dépend pas d'EF Core.
/// </summary>
public interface IRepository<T> : IRepositoryBase<T> where T : Entity;
