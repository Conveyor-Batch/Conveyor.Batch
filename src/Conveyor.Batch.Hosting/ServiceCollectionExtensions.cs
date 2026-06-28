using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.Hosting;

/// <summary>
/// Extension methods for registering Conveyor.Batch services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Conveyor.Batch services with the DI container using the default
    /// <see cref="InMemoryJobRepository"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConveyorBatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobRepository, InMemoryJobRepository>();
        services.AddScoped<IJobLauncher, SimpleJobLauncher>();

        return services;
    }

    /// <summary>
    /// Registers core Conveyor.Batch services with the DI container using a custom
    /// <typeparamref name="TRepository"/> as the <see cref="IJobRepository"/>.
    /// </summary>
    /// <typeparam name="TRepository">The concrete job repository type to register.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConveyorBatch<TRepository>(this IServiceCollection services)
        where TRepository : class, IJobRepository
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobRepository, TRepository>();
        services.AddScoped<IJobLauncher, SimpleJobLauncher>();

        return services;
    }
}
