using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job.Flow;

/// <summary>
/// Identifies what a <see cref="FlowJob"/> should do when a step finishes with a particular
/// exit status.
/// </summary>
internal enum FlowAction
{
    /// <summary>Execute <see cref="TransitionRule.NextStep"/> next.</summary>
    Continue,

    /// <summary>End the job, using the step's own exit status as the job's final status.</summary>
    End,

    /// <summary>Fail the entire job.</summary>
    Fail,

    /// <summary>Stop the job cleanly.</summary>
    Stop
}

/// <summary>
/// A single transition rule: "if the previous step exited with <see cref="OnStatus"/>, do
/// <see cref="Action"/> (and, for <see cref="FlowAction.Continue"/>, execute <see cref="NextStep"/>
/// next)".
/// </summary>
internal sealed class TransitionRule
{
    /// <summary>
    /// Gets the exit status this rule matches: an exact status such as <c>"COMPLETED"</c>,
    /// <c>"FAILED"</c>, <c>"STOPPED"</c>, or the wildcard <c>"*"</c>, which matches any status
    /// not already matched by an exact rule.
    /// </summary>
    public required string OnStatus { get; init; }

    /// <summary>
    /// Gets the step to execute next when this rule matches, for <see cref="FlowAction.Continue"/>
    /// rules. <see langword="null"/> for terminal rules (<see cref="FlowAction.End"/>,
    /// <see cref="FlowAction.Fail"/>, <see cref="FlowAction.Stop"/>).
    /// </summary>
    public IStep? NextStep { get; init; }

    /// <summary>Gets the action to perform when this rule matches.</summary>
    public required FlowAction Action { get; init; }
}
