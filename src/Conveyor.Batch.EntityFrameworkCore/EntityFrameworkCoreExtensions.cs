using Conveyor.Batch.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering Conveyor.Batch EF Core services with the DI container.
/// </summary>
public static class EntityFrameworkCoreExtensions
{
    /// <summary>Registers ConveyorBatchDbContext and EfCoreJobRepository with the DI container.</summary>
    /// <typeparam name="TProvider">Marker type for the database provider (unused, retained for API symmetry).</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureDb">Action to configure the <see cref="DbContextOptionsBuilder"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConveyorBatchEntityFrameworkCore<TProvider>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
        where TProvider : class
    {
        services.AddDbContext<ConveyorBatchDbContext>(configureDb);
        services.AddScoped<IJobRepository, EfCoreJobRepository>();
        return services;
    }
}
