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
    /// <param name="configure">
    /// Optional callback to configure <see cref="ConveyorBatchOptions"/>, e.g. to enable a job
    /// heartbeat via <c>options.HeartbeatInterval = TimeSpan.FromSeconds(30)</c>. When omitted,
    /// heartbeat stays disabled.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConveyorBatch(
        this IServiceCollection services,
        Action<ConveyorBatchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobRepository, InMemoryJobRepository>();
        RegisterHeartbeatOptions(services, configure);
        services.AddScoped<IJobLauncher, SimpleJobLauncher>();

        return services;
    }

    /// <summary>
    /// Registers core Conveyor.Batch services with the DI container using a custom
    /// <typeparamref name="TRepository"/> as the <see cref="IJobRepository"/>.
    /// </summary>
    /// <typeparam name="TRepository">The concrete job repository type to register.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">
    /// Optional callback to configure <see cref="ConveyorBatchOptions"/>, e.g. to enable a job
    /// heartbeat via <c>options.HeartbeatInterval = TimeSpan.FromSeconds(30)</c>. When omitted,
    /// heartbeat stays disabled.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConveyorBatch<TRepository>(
        this IServiceCollection services,
        Action<ConveyorBatchOptions>? configure = null)
        where TRepository : class, IJobRepository
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobRepository, TRepository>();
        RegisterHeartbeatOptions(services, configure);
        services.AddScoped<IJobLauncher, SimpleJobLauncher>();

        return services;
    }

    /// <summary>
    /// Registers a <see cref="HeartbeatOptions"/> singleton only when the caller opted in via
    /// <paramref name="configure"/>. Registering it unconditionally (even with default values)
    /// would silently enable a heartbeat for every existing caller of the parameterless
    /// <c>AddConveyorBatch()</c> overload, since <see cref="SimpleJobLauncher"/> resolves whatever
    /// <see cref="HeartbeatOptions"/> the container provides.
    /// </summary>
    private static void RegisterHeartbeatOptions(IServiceCollection services, Action<ConveyorBatchOptions>? configure)
    {
        if (configure is null)
            return;

        var options = new ConveyorBatchOptions();
        configure(options);
        services.AddSingleton(new HeartbeatOptions { Interval = options.HeartbeatInterval });
    }
}
