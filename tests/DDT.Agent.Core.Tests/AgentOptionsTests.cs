// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentOptionsTests
{
    [Fact]
    public void AcceptsTheArgumentAnOlderAgentPassesLast()
    {
        // What an agent from an older boot image passes to the newer agent it starts.
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", AgentOptions.NoUpdateArgument], out AgentOptions? options, out string error), error);
        Assert.True(options!.NoUpdate);
    }

    [Fact]
    public void GivesTheDryRunsMachineSecureBootOnlyWhenAsked()
    {
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--dry-run"], out AgentOptions? plain, out string error), error);
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--dry-run", "--dry-run-secure-boot"], out AgentOptions? secure, out error), error);

        Assert.False(plain!.DryRunSecureBoot);
        Assert.True(secure!.DryRunSecureBoot);
    }

    // Without the argument, the agent's own start-up takes the boot environment it is built for.
    [Fact]
    public void LetsADryRunStartTheBootEnvironmentItNames()
    {
        string[] dryRun = ["--server", "https://ddt.example:7152", "--dry-run"];

        Assert.True(AgentOptions.TryParse(dryRun, out AgentOptions? plain, out string error), error);
        Assert.True(AgentOptions.TryParse([.. dryRun, "--dry-run-environment", "LinuxBoot"], out AgentOptions? linux, out error), error);
        Assert.True(AgentOptions.TryParse(["--dry-run-environment", "windowspe", .. dryRun], out AgentOptions? windowsPE, out error), error);

        Assert.Null(plain!.DryRunEnvironment);
        Assert.Equal(AgentEnvironment.LinuxBoot, linux!.DryRunEnvironment);
        Assert.Equal(AgentEnvironment.WindowsPE, windowsPE!.DryRunEnvironment);
    }

    // The executable for Linux stands in for its own boot environment only, and names neither Windows PE nor the
    // graphical console in what it prints.
    [Fact]
    public void TheExecutableForLinuxEndsARefusalWithItsOwnUsageAndHasNoDryRunOfWindowsPE()
    {
        string[] dryRun = ["--server", "https://ddt.example:7152", "--dry-run"];

        Assert.True(AgentOptions.TryParse([.. dryRun, "--dry-run-environment", "LinuxBoot"], AgentEnvironment.LinuxBoot, out AgentOptions? own, out string error), error);
        Assert.False(AgentOptions.TryParse([.. dryRun, "--dry-run-environment", "WindowsPE"], AgentEnvironment.LinuxBoot, out _, out string other));
        Assert.False(AgentOptions.TryParse(["--server", "http://ddt.example"], AgentEnvironment.LinuxBoot, out _, out string plain));

        Assert.Equal(AgentEnvironment.LinuxBoot, own!.DryRunEnvironment);
        Assert.Equal($"Unknown or invalid argument --dry-run-environment. {EnvironmentTexts.LinuxUsage}", other);
        Assert.Equal($"An https server URL is required. {EnvironmentTexts.LinuxUsage}", plain);
        Assert.DoesNotContain("WindowsPE", EnvironmentTexts.LinuxUsage, StringComparison.Ordinal);
        Assert.DoesNotContain(".exe", EnvironmentTexts.LinuxUsage, StringComparison.Ordinal);
    }

    [Fact]
    public void TurnsDownADryRunEnvironmentThatIsNoBootEnvironmentOrComesWithoutADryRun()
    {
        string[] dryRun = ["--server", "https://ddt.example:7152", "--dry-run"];

        // A run starts in a boot environment, never in the installed Windows.
        Assert.False(AgentOptions.TryParse([.. dryRun, "--dry-run-environment", "Windows"], out _, out string installed));
        Assert.False(AgentOptions.TryParse([.. dryRun, "--dry-run-environment", "2"], out _, out string number));
        Assert.False(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--dry-run-environment", "LinuxBoot"], out _, out string alone));

        Assert.StartsWith("Unknown or invalid argument --dry-run-environment.", installed, StringComparison.Ordinal);
        Assert.StartsWith("Unknown or invalid argument --dry-run-environment.", number, StringComparison.Ordinal);
        Assert.StartsWith("--dry-run-environment goes with --dry-run.", alone, StringComparison.Ordinal);
        Assert.EndsWith(AgentOptions.Usage, alone, StringComparison.Ordinal);
        Assert.Contains("[--dry-run-environment <WindowsPE|LinuxBoot>]", AgentOptions.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesAGraphicalConsoleOnlyWhenAsked()
    {
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--dry-run"], out AgentOptions? plain, out string error), error);
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--console", @"C:\Tools\ddt-console.exe"], out AgentOptions? named, out error), error);

        Assert.Null(plain!.ConsolePath);
        Assert.Equal(@"C:\Tools\ddt-console.exe", named!.ConsolePath);
        Assert.False(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--console"], out _, out error));
        Assert.StartsWith("--console needs a value.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAnAgentJsonWrittenWhileEnrollmentTokensExisted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "serverUrl": "https://ddt.example:7152", "enrollmentToken": "ddt1.x.y", "rootCertificate": null }""");

        try
        {
            Assert.True(AgentOptions.TryParse(["--config", path], out AgentOptions? options, out string error), error);
            Assert.Equal(new Uri("https://ddt.example:7152"), options!.ServerUrl);
            Assert.False(options.NoUpdate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TurnsDownAnArgumentWithoutItsValue()
    {
        Assert.False(AgentOptions.TryParse(["--dry-run", "--server"], out AgentOptions? options, out string error));

        Assert.Null(options);
        Assert.StartsWith("--server needs a value.", error, StringComparison.Ordinal);
        Assert.EndsWith(AgentOptions.Usage, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TurnsDownAnArgumentItDoesNotKnow()
    {
        Assert.False(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--colour", "blue"], out _, out string unknown));
        Assert.False(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--dry-run-id", "first"], out _, out string invalid));

        Assert.StartsWith("Unknown or invalid argument --colour.", unknown, StringComparison.Ordinal);
        Assert.StartsWith("Unknown or invalid argument --dry-run-id.", invalid, StringComparison.Ordinal);
    }

    [Fact]
    public void TurnsDownAnAgentJsonThatIsNotJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "serverUrl": "https://ddt.example:7152" """);

        try
        {
            Assert.False(AgentOptions.TryParse(["--config", path], out AgentOptions? options, out string error));

            Assert.Null(options);
            Assert.StartsWith($"{path} is not valid: ", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RequiresAnHttpsServerUrl()
    {
        Assert.False(AgentOptions.TryParse(["--server", "http://ddt.example:7152"], out AgentOptions? options, out string error));

        Assert.Null(options);
        Assert.StartsWith("An https server URL is required.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TurnsDownARootCertificateThatIsNotPem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, "not a certificate");

        try
        {
            Assert.False(AgentOptions.TryParse(["--server", "https://ddt.example:7152", "--root-certificate", path], out _, out string error));

            Assert.StartsWith("The root certificate is not a PEM certificate: ", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
