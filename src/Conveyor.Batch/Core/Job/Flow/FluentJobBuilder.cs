using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job.Flow;

/// <summary>
/// Fluent builder for constructing an <see cref="IJob"/> whose steps form a directed graph:
/// each step's next step is chosen at runtime based on the exit status of the previous step.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Start"/> to register the first step and open its transition block, then chain
/// <see cref="On"/> to declare a rule for a specific exit status, closing the rule with
/// <see cref="TransitionBlock.To"/>, <see cref="TransitionBlock.End"/>,
/// <see cref="TransitionBlock.Fail"/>, or <see cref="TransitionBlock.Stop"/>. Use
/// <see cref="From"/> to open a transition block for a step that was already referenced by a
/// prior <see cref="TransitionBlock.To"/> call. Call <see cref="Build"/> to validate the graph
/// and produce the finished <see cref="IJob"/>.
/// </para>
/// <example>
/// <code>
/// var job = new FluentJobBuilder("import-and-notify", repository)
///     .Start(validateStep)
///         .On("COMPLETED").To(importStep)
///         .On("FAILED").To(validationErrorStep)
///     .From(importStep)
///         .On("COMPLETED").To(notifyStep)
///         .On("FAILED").Fail()
///     .From(validationErrorStep)
///         .On("*").End()
///     .From(notifyStep)
///         .On("*").End()
///     .Build();
/// </code>
/// </example>
/// <para>
/// This builder is a separate, independent alternative to <see cref="JobBuilder"/> for jobs that
/// need conditional branching; <see cref="JobBuilder"/> remains unchanged for simple sequential jobs.
/// </para>
/// </remarks>
public sealed class FluentJobBuilder
{
    private readonly string _name;
    private readonly IJobRepository _repository;
    private readonly Dictionary<IStep, StepTransitions> _transitions = [];
    private IStep? _startStep;
    private TransitionBlock? _currentBlock;

    /// <summary>
    /// Initializes a new <see cref="FluentJobBuilder"/> with the given job name and repository.
    /// </summary>
    /// <param name="name">The unique name of the job.</param>
    /// <param name="repository">The job repository used to persist execution state.</param>
    public FluentJobBuilder(string name, IJobRepository repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(repository);
        _name = name;
        _repository = repository;
    }

    /// <summary>
    /// Registers <paramref name="step"/> as the first step of the job and opens its transition
    /// block. Call exactly once, before any <see cref="From"/> calls.
    /// </summary>
    /// <param name="step">The step to execute first.</param>
    /// <returns>This builder, so that <see cref="On"/> can be chained to declare rules for <paramref name="step"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Start"/> has already been called.</exception>
    public FluentJobBuilder Start(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_startStep is not null)
            throw new InvalidOperationException("Start(...) has already been called for this builder.");

        _startStep = step;
        OpenBlock(step);
        return this;
    }

    /// <summary>
    /// Opens a transition block for <paramref name="step"/>, which must have already been
    /// registered (as the start step, or as the target of a prior <see cref="TransitionBlock.To"/> call).
    /// </summary>
    /// <param name="step">The step to declare transition rules for.</param>
    /// <returns>This builder, so that <see cref="On"/> can be chained to declare rules for <paramref name="step"/>.</returns>
    public FluentJobBuilder From(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        OpenBlock(step);
        return this;
    }

    /// <summary>
    /// Opens a transition rule for the current step (the step most recently passed to
    /// <see cref="Start"/> or <see cref="From"/>), matching exit status <paramref name="status"/>.
    /// </summary>
    /// <param name="status">
    /// The exit status to match: <c>"COMPLETED"</c>, <c>"FAILED"</c>, <c>"STOPPED"</c>, or the
    /// wildcard <c>"*"</c>, which matches any status not already matched by an exact rule.
    /// </param>
    /// <returns>
    /// A <see cref="TransitionBlock"/> exposing <see cref="TransitionBlock.To"/>,
    /// <see cref="TransitionBlock.End"/>, <see cref="TransitionBlock.Fail"/>, and
    /// <see cref="TransitionBlock.Stop"/> to close this rule.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="On"/> is called before <see cref="Start"/> or <see cref="From"/>.
    /// </exception>
    public TransitionBlock On(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        if (_currentBlock is null)
            throw new InvalidOperationException("On(...) must be called after Start(...) or From(...).");

        _currentBlock.PendingStatus = status;
        return _currentBlock;
    }

    /// <summary>
    /// Validates the configured transition graph and builds the resulting <see cref="IJob"/>.
    /// </summary>
    /// <returns>An <see cref="IJob"/> that executes the configured step graph.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Start"/> was never called, if any registered step has no transition
    /// rules, if any <see cref="TransitionBlock.To"/> target has no corresponding
    /// <see cref="From"/>/<see cref="Start"/> block, if any registered step is unreachable from
    /// the start step, or if no terminal rule (<see cref="TransitionBlock.End"/>,
    /// <see cref="TransitionBlock.Fail"/>, <see cref="TransitionBlock.Stop"/>) is reachable from
    /// the start step.
    /// </exception>
    public IJob Build()
    {
        if (_startStep is null)
            throw new InvalidOperationException("Start(...) must be called before Build().");

        foreach (var transitions in _transitions.Values)
        {
            if (transitions.Rules.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Step '{transitions.Step.Name}' has no transition rules defined. " +
                    "Call .On(...) at least once after .Start()/.From() for this step.");
            }
        }

        foreach (var transitions in _transitions.Values)
        {
            foreach (var rule in transitions.Rules)
            {
                if (rule.Action == FlowAction.Continue && !_transitions.ContainsKey(rule.NextStep!))
                {
                    throw new InvalidOperationException(
                        $"Step '{rule.NextStep!.Name}' is referenced by a .To(...) transition " +
                        "but has no .From(...) block defined.");
                }
            }
        }

        var reachable = CollectReachableSteps(_startStep, _transitions);
        foreach (var step in _transitions.Keys)
        {
            if (!ReferenceEquals(step, _startStep) && !reachable.Contains(step))
            {
                throw new InvalidOperationException(
                    $"Step '{step.Name}' is registered but is not reachable from the start step '{_startStep.Name}'.");
            }
        }

        if (!IsTerminalReachable(_startStep, _transitions))
        {
            throw new InvalidOperationException(
                "No terminal rule (.End()/.Fail()/.Stop()) is reachable from the start step " +
                $"'{_startStep.Name}'. Every path from the start step must eventually reach a terminal rule.");
        }

        return new FlowJob(_name, _startStep, _transitions, _repository);
    }

    private void OpenBlock(IStep step)
    {
        if (!_transitions.TryGetValue(step, out var transitions))
        {
            transitions = new StepTransitions { Step = step };
            _transitions[step] = transitions;
        }

        _currentBlock = new TransitionBlock(this, transitions);
    }

    private static HashSet<IStep> CollectReachableSteps(
        IStep start, IReadOnlyDictionary<IStep, StepTransitions> transitions)
    {
        var visited = new HashSet<IStep>();
        var stack = new Stack<IStep>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var step = stack.Pop();
            if (!visited.Add(step))
                continue;

            if (!transitions.TryGetValue(step, out var stepTransitions))
                continue;

            foreach (var rule in stepTransitions.Rules)
            {
                if (rule.Action == FlowAction.Continue && rule.NextStep is not null)
                    stack.Push(rule.NextStep);
            }
        }

        return visited;
    }

    private static bool IsTerminalReachable(
        IStep start, IReadOnlyDictionary<IStep, StepTransitions> transitions)
    {
        var visited = new HashSet<IStep>();
        var stack = new Stack<IStep>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var step = stack.Pop();
            if (!visited.Add(step))
                continue;

            if (!transitions.TryGetValue(step, out var stepTransitions))
                continue;

            foreach (var rule in stepTransitions.Rules)
            {
                if (rule.Action != FlowAction.Continue)
                    return true;

                if (rule.NextStep is not null)
                    stack.Push(rule.NextStep);
            }
        }

        return false;
    }

    /// <summary>
    /// Exposes the ways to close a transition rule opened by <see cref="FluentJobBuilder.On"/>:
    /// continue to another step (<see cref="To"/>), end the job (<see cref="End"/>), fail the job
    /// (<see cref="Fail"/>), or stop the job (<see cref="Stop"/>). Each closing call returns the
    /// parent <see cref="FluentJobBuilder"/>, so further <see cref="FluentJobBuilder.On"/>,
    /// <see cref="FluentJobBuilder.From"/>, or <see cref="FluentJobBuilder.Build"/> calls can be
    /// chained directly.
    /// </summary>
    public sealed class TransitionBlock
    {
        private readonly FluentJobBuilder _parent;
        private readonly StepTransitions _transitions;

        internal string? PendingStatus { get; set; }

        internal TransitionBlock(FluentJobBuilder parent, StepTransitions transitions)
        {
            _parent = parent;
            _transitions = transitions;
        }

        /// <summary>
        /// Closes the current rule: when the step exits with the status passed to
        /// <see cref="FluentJobBuilder.On"/>, execute <paramref name="nextStep"/> next.
        /// </summary>
        /// <param name="nextStep">The step to execute next.</param>
        /// <returns>The parent <see cref="FluentJobBuilder"/>, for further chaining.</returns>
        public FluentJobBuilder To(IStep nextStep)
        {
            ArgumentNullException.ThrowIfNull(nextStep);
            AddRule(FlowAction.Continue, nextStep);
            return _parent;
        }

        /// <summary>
        /// Closes the current rule: when the step exits with the status passed to
        /// <see cref="FluentJobBuilder.On"/>, end the job using the step's own exit status as the
        /// job's final status.
        /// </summary>
        /// <returns>The parent <see cref="FluentJobBuilder"/>, for further chaining.</returns>
        public FluentJobBuilder End()
        {
            AddRule(FlowAction.End, nextStep: null);
            return _parent;
        }

        /// <summary>
        /// Closes the current rule: when the step exits with the status passed to
        /// <see cref="FluentJobBuilder.On"/>, fail the entire job.
        /// </summary>
        /// <returns>The parent <see cref="FluentJobBuilder"/>, for further chaining.</returns>
        public FluentJobBuilder Fail()
        {
            AddRule(FlowAction.Fail, nextStep: null);
            return _parent;
        }

        /// <summary>
        /// Closes the current rule: when the step exits with the status passed to
        /// <see cref="FluentJobBuilder.On"/>, stop the job cleanly.
        /// </summary>
        /// <returns>The parent <see cref="FluentJobBuilder"/>, for further chaining.</returns>
        public FluentJobBuilder Stop()
        {
            AddRule(FlowAction.Stop, nextStep: null);
            return _parent;
        }

        private void AddRule(FlowAction action, IStep? nextStep)
        {
            if (PendingStatus is null)
            {
                throw new InvalidOperationException(
                    "To()/End()/Fail()/Stop() must be preceded by a call to On(...).");
            }

            _transitions.Rules.Add(new TransitionRule { OnStatus = PendingStatus, NextStep = nextStep, Action = action });
            PendingStatus = null;
        }
    }
}
