using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Repository;

/// <summary>Factory methods for creating job repository instances.</summary>
public static class JobRepositories
{
    /// <summary>Creates a new in-memory job repository suitable for testing and single-process scenarios.</summary>
    public static IJobRepository InMemory() => new InMemoryJobRepository();
}
