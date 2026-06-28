namespace Conveyor.Batch.Policies;

/// <summary>
/// Classifies exceptions as skippable or retryable based on registered exception types.
/// </summary>
public sealed class ExceptionClassifier
{
    private readonly HashSet<Type> _skippable = [];
    private readonly HashSet<Type> _retryable = [];

    /// <summary>Registers an exception type as skippable.</summary>
    public ExceptionClassifier AddSkippable<TException>() where TException : Exception
    {
        _skippable.Add(typeof(TException));
        return this;
    }

    /// <summary>Registers an exception type as retryable.</summary>
    public ExceptionClassifier AddRetryable<TException>() where TException : Exception
    {
        _retryable.Add(typeof(TException));
        return this;
    }

    /// <summary>Returns <see langword="true"/> if the exception type is registered as skippable.</summary>
    public bool IsSkippable(Exception exception) => IsMatch(_skippable, exception);

    /// <summary>Returns <see langword="true"/> if the exception type is registered as retryable.</summary>
    public bool IsRetryable(Exception exception) => IsMatch(_retryable, exception);

    private static bool IsMatch(HashSet<Type> set, Exception exception)
    {
        var type = exception.GetType();
        foreach (var registered in set)
        {
            if (registered.IsAssignableFrom(type))
                return true;
        }
        return false;
    }
}
