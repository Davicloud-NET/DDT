// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
