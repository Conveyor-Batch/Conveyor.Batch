using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.UnitTests.Job;

public sealed class JobParametersEqualityTests
{
    [Fact]
    public void SameContents_DifferentInstances_AreEqual()
    {
        var a = new JobParameters(new Dictionary<string, string> { ["file"] = "a", ["date"] = "b" });
        var b = new JobParameters(new Dictionary<string, string> { ["file"] = "a", ["date"] = "b" });

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void DifferentContents_AreNotEqual()
    {
        var a = new JobParameters(new Dictionary<string, string> { ["file"] = "a" });
        var b = new JobParameters(new Dictionary<string, string> { ["file"] = "b" });

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void KeyOrderIndependent_AreEqual()
    {
        var a = new JobParameters(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        var b = new JobParameters(new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_Equals_Empty()
    {
        var a = JobParameters.Empty;
        var b = new JobParameters(new Dictionary<string, string>());

        Assert.Equal(a, b);
    }

    [Fact]
    public void GetHashCode_SameContents_SameHash()
    {
        var a = new JobParameters(new Dictionary<string, string> { ["file"] = "a", ["date"] = "b" });
        var b = new JobParameters(new Dictionary<string, string> { ["date"] = "b", ["file"] = "a" });

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
