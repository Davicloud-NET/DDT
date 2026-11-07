// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// What the run's log gets of how cloud-init ended, from the result.json and status.json two real images wrote in
// the tries before this step: Debian 13 with cloud-init 25.1.4 and Ubuntu 24.04 with 26.1.
public sealed class CloudInitReportTests : IDisposable
{
    private readonly CloudInitRoot _linux = new();

    public void Dispose() => _linux.Dispose();

    private string[] Written(string image)
    {
        CloudInitReport.Write(CloudInitRoot.Fixture(image, "result.json"), CloudInitRoot.Fixture(image, "status.json"), _linux.Log);

        return _linux.Lines();
    }

    // The two versions name the module differently, and both say the same of it.
    [Theory]
    [InlineData("debian-13-runcmd-failed", "scripts-user")]
    [InlineData("ubuntu-24.04-runcmd-failed", "scripts_user")]
    public void SaysWhatARuncmdThatFailedLeftOnARealImage(string image, string module)
    {
        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
                $"WARN  cloud-init ended with an error: ('{module}', RuntimeError('Runparts: 1 failures (runcmd) in 1 attempted commands'))",
                $"WARN  cloud-init WARNING: Failed to run module {module} (scripts in /var/lib/cloud/instance/scripts)",
                $"WARN  cloud-init WARNING: Running module {module} (<module 'cloudinit.config.cc_scripts_user' from " +
                    "'/usr/lib/python3/dist-packages/cloudinit/config/cc_scripts_user.py'>) failed",
                $"INFO  {CloudInitReport.OutputStays}",
            ],
            Written(image));
    }

    // User-data that is not YAML ends cloud-init without an error. Only the stages' warnings show it, each of them
    // several lines long and repeated by stage after stage.
    [Theory]
    [InlineData("debian-13-not-yaml", 3)]
    [InlineData("ubuntu-24.04-not-yaml", 2)]
    public void SaysEachWarningOfUserDataThatIsNotYamlOnceAndInOneLine(string image, int warnings)
    {
        string[] lines = Written(image);

        Assert.Equal(warnings + 2, lines.Length);
        Assert.All(lines[1..^1], line => Assert.StartsWith("WARN  cloud-init WARNING: ", line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("WARN  cloud-init WARNING: Failed loading yaml blob. Invalid format at line 4 column 1: \"while scanning for the next token found character", StringComparison.Ordinal));
        Assert.Contains("WARN  cloud-init WARNING: Failed at merging in cloud config part from part-001: empty cloud config", lines);
        Assert.All(lines, line => Assert.DoesNotContain(line, character => char.IsControl(character)));
        Assert.Equal(lines.Length, lines.Distinct().Count());
    }

    [Fact]
    public void AStatusWithOneWarningInTwoStagesSaysItOnce()
    {
        string[] lines = Written("debian-13-schema-warning");

        Assert.Equal(
            "WARN  cloud-init WARNING: cloud-config failed schema validation! You may run 'sudo cloud-init schema --system' to check the details.",
            Assert.Single(lines[1..^1]));
    }

    // A run of a module over many files can fail many times. The log takes twenty and says how many more there are.
    [Fact]
    public void TakesTwentyWarningsAndSaysHowManyMoreThereAre()
    {
        string result = JsonSerializer.Serialize(new { v1 = new { errors = Enumerable.Range(1, 23).Select(number => $"error {number}") } });
        string status = """{ "v1": { "init": { "recoverable_errors": { "WARNING": [ "warning 1", "warning 2" ] } }, "stage": null } }""";

        CloudInitReport.Write(result, status, _linux.Log);

        string[] lines = _linux.Lines();
        Assert.Equal(CloudInitReport.MaxWarnings + 3, lines.Length);
        Assert.Equal("WARN  cloud-init ended with an error: error 20", lines[20]);
        Assert.Equal("WARN  cloud-init reported 5 more. cloud-init status --long lists them on the machine.", lines[21]);
    }

    // A traceback in an error, or a script's whole output in one.
    [Fact]
    public void CutsAMessageThatIsLongerThanALineOfTheLogShouldBe()
    {
        string result = JsonSerializer.Serialize(new { v1 = new { errors = new[] { new string('x', 2000) } } });

        CloudInitReport.Write(result, null, _linux.Log);

        Assert.Equal($"WARN  cloud-init ended with an error: {new string('x', 500)}", _linux.Lines()[1]);
    }
}
