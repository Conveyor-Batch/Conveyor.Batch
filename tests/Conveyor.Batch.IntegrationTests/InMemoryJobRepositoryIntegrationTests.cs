using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IntegrationTests;

public sealed class InMemoryJobRepositoryIntegrationTests
{
    private static InMemoryJobRepository CreateRepository() => new();

    private static JobParameters MakeParams(string key = "run", string value = "1") =>
        new(new Dictionary<string, string> { [key] = value });

    [Fact]
    public async Task FullLifecycle_CreateAndUpdateJobAndStep_StateIsConsistent()
    {
        var repo = CreateRepository();
        var parameters = MakeParams();

        // Create job instance and execution
        var instance = await repo.CreateJobInstanceAsync("TestJob", parameters);
        Assert.Equal("TestJob", instance.JobName);
        Assert.True(instance.Id > 0);

        var execution = await repo.CreateJobExecutionAsync(instance, parameters);
        Assert.Equal(BatchStatus.Starting, execution.Status);
        Assert.Equal(instance.Id, execution.JobInstance.Id);

        // Update execution status
        execution.Status = BatchStatus.Started;
        await repo.UpdateJobExecutionAsync(execution);

        // Create and update step execution
        var stepExec = await repo.CreateStepExecutionAsync(execution, "step1");
        Assert.Equal(BatchStatus.Starting, stepExec.Status);
        Assert.Equal("step1", stepExec.StepName);

        stepExec.Status = BatchStatus.Completed;
        await repo.UpdateStepExecutionAsync(stepExec);

        // Finalize job
        execution.Status = BatchStatus.Completed;
        execution.EndTime = DateTimeOffset.UtcNow;
        await repo.UpdateJobExecutionAsync(execution);

        // Verify retrieval
        var last = await repo.GetLastJobExecutionAsync("TestJob", parameters);
        Assert.NotNull(last);
        Assert.Equal(BatchStatus.Completed, last.Status);
        Assert.NotNull(last.EndTime);
    }

    [Fact]
    public async Task GetLastJobExecutionAsync_ReturnsLatestExecution()
    {
        var repo = CreateRepository();
        var parameters = MakeParams();

        var instance = await repo.CreateJobInstanceAsync("MyJob", parameters);

        // Create first execution
        var exec1 = await repo.CreateJobExecutionAsync(instance, parameters);
        exec1.Status = BatchStatus.Completed;
        await repo.UpdateJobExecutionAsync(exec1);

        // Small delay so StartTime differs
        await Task.Delay(10);

        // Create second execution
        var exec2 = await repo.CreateJobExecutionAsync(instance, parameters);
        exec2.Status = BatchStatus.Completed;
        await repo.UpdateJobExecutionAsync(exec2);

        var last = await repo.GetLastJobExecutionAsync("MyJob", parameters);
        Assert.NotNull(last);
        Assert.Equal(exec2.Id, last.Id);
    }

    [Fact]
    public async Task GetJobExecutionsAsync_ReturnsAllExecutionsForInstance()
    {
        var repo = CreateRepository();
        var parameters = MakeParams();

        var instance = await repo.CreateJobInstanceAsync("MultiRunJob", parameters);

        var ids = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var exec = await repo.CreateJobExecutionAsync(instance, parameters);
            exec.Status = BatchStatus.Completed;
            await repo.UpdateJobExecutionAsync(exec);
            ids.Add(exec.Id);
        }

        var executions = await repo.GetJobExecutionsAsync(instance);
        Assert.Equal(3, executions.Count);
        Assert.All(ids, id => Assert.Contains(executions, e => e.Id == id));
    }

    [Fact]
    public async Task GetJobExecutionsAsync_DoesNotReturnExecutionsFromDifferentInstance()
    {
        var repo = CreateRepository();
        var params1 = MakeParams("run", "1");
        var params2 = MakeParams("run", "2");

        var instance1 = await repo.CreateJobInstanceAsync("Job", params1);
        var instance2 = await repo.CreateJobInstanceAsync("Job", params2);

        await repo.CreateJobExecutionAsync(instance1, params1);
        await repo.CreateJobExecutionAsync(instance2, params2);

        var execsForInstance1 = await repo.GetJobExecutionsAsync(instance1);
        Assert.Single(execsForInstance1);
        Assert.Equal(instance1.Id, execsForInstance1[0].JobInstance.Id);
    }

    [Fact]
    public async Task ConcurrentStepExecutionCreation_DoesNotCorruptState()
    {
        var repo = CreateRepository();
        var parameters = JobParameters.Empty;

        var instance = await repo.CreateJobInstanceAsync("ConcurrentJob", parameters);
        var execution = await repo.CreateJobExecutionAsync(instance, parameters);

        // Run 10 tasks in parallel, each creating a step execution
        const int taskCount = 10;
        var tasks = Enumerable.Range(0, taskCount).Select(i =>
            repo.CreateStepExecutionAsync(execution, $"step-{i}"));

        var stepExecutions = await Task.WhenAll(tasks);

        // All IDs should be unique
        var ids = stepExecutions.Select(s => s.Id).ToHashSet();
        Assert.Equal(taskCount, ids.Count);

        // All step names should be distinct
        var names = stepExecutions.Select(s => s.StepName).ToHashSet();
        Assert.Equal(taskCount, names.Count);
    }
}
