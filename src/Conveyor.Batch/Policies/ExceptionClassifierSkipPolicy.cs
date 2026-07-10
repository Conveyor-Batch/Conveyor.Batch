namespace Conveyor.Batch.Policies;

/// <summary>
/// An <see cref="ISkipPolicy"/> backed by an <see cref="ExceptionClassifier"/>'s
/// skippable-exception rules, e.g. <c>new ExceptionClassifierSkipPolicy(new
/// ExceptionClassifier().AddSkippable&lt;FormatException&gt;())</c>.
/// </summary>
public sealed class ExceptionClassifierSkipPolicy : ISkipPolicy
{
    private readonly ExceptionClassifier _classifier;

    /// <summary>
    /// Initializes a new <see cref="ExceptionClassifierSkipPolicy"/>.
    /// </summary>
    /// <param name="classifier">The classifier whose skippable-exception rules this policy delegates to.</param>
    public ExceptionClassifierSkipPolicy(ExceptionClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    /// <inheritdoc />
    public bool ShouldSkip(Exception exception, long skipCount) => _classifier.IsSkippable(exception);
}
