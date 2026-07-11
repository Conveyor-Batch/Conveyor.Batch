using Conveyor.Batch.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.Hosting;

/// <summary>
/// Extension methods for integrating batch jobs with the .NET generic host.
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TJob"/> as a transient <see cref="IJob"/> and adds a
    /// <see cref="BatchJobHostedService"/> that runs it on host startup.
    /// </summary>
    /// <typeparam name="TJob">The concrete job type to register and run.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Ensure <see cref="ServiceCollectionExtensions.AddConveyorBatch(IServiceCollection, Action{ConveyorBatchOptions})"/> has
    /// been called before this method so that <see cref="IJobLauncher"/> is registered.
    /// </remarks>
    public static IServiceCollection AddBatchJob<TJob>(this IServiceCollection services)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IJob, TJob>();
        services.AddHostedService<BatchJobHostedService>();

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TJob"/> exactly as
    /// <see cref="AddBatchJob{TJob}(IServiceCollection)"/> does, additionally invoking
    /// <paramref name="configure"/> against the constructed <see cref="BatchJobHostedService"/>
    /// so callers can customize its shutdown behavior, e.g.:
    /// <c>services.AddBatchJob&lt;MyJob&gt;(service => service.ShutdownTimeout = TimeSpan.FromSeconds(60));</c>
    /// </summary>
    /// <typeparam name="TJob">The concrete job type to register and run.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Callback invoked with the constructed hosted service instance.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Ensure <see cref="ServiceCollectionExtensions.AddConveyorBatch(IServiceCollection, Action{ConveyorBatchOptions})"/> has
    /// been called before this method so that <see cref="IJobLauncher"/> is registered.
    /// </remarks>
    public static IServiceCollection AddBatchJob<TJob>(
        this IServiceCollection services,
        Action<BatchJobHostedService> configure)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTransient<IJob, TJob>();
        services.AddHostedService<BatchJobHostedService>(sp =>
        {
            var service = ActivatorUtilities.CreateInstance<BatchJobHostedService>(sp);
            configure(service);
            return service;
        });

        return services;
    }
}
