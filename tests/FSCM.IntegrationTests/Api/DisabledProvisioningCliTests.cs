using System.Diagnostics;
using FSCM.API.Security;

namespace FSCM.IntegrationTests.Api;

public sealed class DisabledProvisioningCliTests
{
    [Theory]
    [InlineData("--seed")]
    [InlineData("--seed=true")]
    [InlineData("--BOOTSTRAP-ADMIN")]
    [InlineData("--bootstrap-admin=true")]
    public async Task Retired_commands_fail_before_configuration_or_database_access(string command)
    {
        var api = typeof(ScreenPermissionMap).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(api);
        start.ArgumentList.Add(command);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Backend data seeding is disabled", await output + await error);
    }

    [Fact]
    public void Production_persistence_assembly_contains_no_provisioning_seeders()
    {
        var types = typeof(FSCM.Persistence.DbContexts.AppDbContext).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => type.Namespace == "FSCM.Persistence.Seeding");
    }
}
