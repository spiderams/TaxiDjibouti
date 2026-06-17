using Ardalis.Specification.EntityFrameworkCore;
using Taxi.Application.Abstractions;
using Taxi.SharedKernel;

namespace Taxi.Infrastructure.Persistence;

/// <summary>
/// Implémentation générique du dépôt (Ardalis) au-dessus d'EF Core.
/// </summary>
public sealed class Repository<T>(AppDbContext context)
    : RepositoryBase<T>(context), IRepository<T> where T : Entity;
