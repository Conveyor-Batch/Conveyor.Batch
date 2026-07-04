using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job.Flow;

/// <summary>
/// The set of <see cref="TransitionRule"/>s configured for a single step, i.e. every
/// "<c>.On(status)...</c>" rule registered against that step's <c>.Start()</c>/<c>.From()</c> block.
/// </summary>
internal sealed class StepTransitions
{
    /// <summary>Gets the step these transitions apply to.</summary>
    public required IStep Step { get; init; }

    /// <summary>Gets the transition rules registered for <see cref="Step"/>, in registration order.</summary>
    public List<TransitionRule> Rules { get; } = [];

    /// <summary>
    /// Finds the rule that applies to the given exit status. An exact match (e.g. rule
    /// <c>OnStatus == "FAILED"</c> against exit status <c>"FAILED"</c>) always takes priority over
    /// a wildcard (<c>OnStatus == "*"</c>) rule, regardless of registration order.
    /// </summary>
    /// <param name="exitStatus">The exit status produced by the step's last execution.</param>
    /// <returns>The matching rule, or <see langword="null"/> if no rule applies.</returns>
    public TransitionRule? Match(string exitStatus)
    {
        TransitionRule? wildcard = null;

        foreach (var rule in Rules)
        {
            if (rule.OnStatus == exitStatus)
                return rule;

            if (rule.OnStatus == "*")
                wildcard ??= rule;
        }

        return wildcard;
    }
}
