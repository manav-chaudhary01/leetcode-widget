namespace LeetCodeDesktopWidget.Tests;

public class InfrastructureTests
{
    [Fact]
    public void TestEnvironment_TargetsNet10AndWindows()
    {
        // Verify .NET runtime version is 10 or greater
        Assert.True(Environment.Version.Major >= 10, $"Expected .NET 10 or greater, but found {Environment.Version}");
        Assert.True(OperatingSystem.IsWindows(), "Application must run on Windows platform.");
    }
}
