// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Agent.Servicing;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ServicePackagesStepRunnerTests : IDisposable
{
    private const string Dism = @"X:\Windows\System32\dism.exe";
    private const string LanguagePack = "Microsoft-Windows-Client-Language-Pack_x64_de-de.cab";
    private const string Basic = "Microsoft-Windows-LanguageFeatures-Basic-de-de-Package~31bf3856ad364e35~amd64~~.cab";
    private const string Checkpoint = "windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu";
    private const string Target = "windows11.0-kb5132061-x64_aedc878e9e078df2ec7e2cfb98dd5b21831cdbcc.msu";
    private const string Older = "windows11.0-kb5040000-x64.msu";

    private static readonly ServicePackagesStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b8"),
        Name = "Service packages",
    };

    private readonly TestZip _update = new((Checkpoint, "checkpoint"), (Target, "target"));
    private readonly TestZip _german = new((LanguagePack, "pack"), (Basic, "basic"));
    private readonly TestZip _older = new((Older, "older"));
    private readonly FakeFreeSpace _space = new();
    private readonly StepRunnerFixture _run;

    public ServicePackagesStepRunnerTests()
    {
        _run = new StepRunnerFixture(
            [s_step],
            packages:
            [
                new AgentRunPackage(s_step.Id, "2026-11 update", _update.Sha256, _update.Content.Length),
                new AgentRunPackage(s_step.Id, "de-DE language pack", _german.Sha256, _german.Content.Length),
                new AgentRunPackage(s_step.Id, "2026-07 update", _older.Sha256, _older.Content.Length) { Matched = true },
            ],
            freeSpace: _space);

        foreach (TestZip zip in new[] { _update, _german, _older })
        {
            _run.Server.ServeFile(zip.Sha256, zip.Content);
        }
    }

    private ServicePackagesStepRunner Runner => new(new DismOfflineServicing(_run.ToolRunner, Dism, _run.Log), _run.Downloads, _run.Session, _run.Log);

    private string Directory => Path.Combine(_run.Tools.Volumes.Windows, "DDT");

    [Fact]
    public async Task AddsLanguagePacksThenFeaturesThenUpdatesLowestKbFirstAndCleansUp()
    {
        TargetVolumes volumes = _run.Partitioned();

        StepResult result = await RunAsync(s_step);

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(
            [
                Add(volumes, 1, Package(1, LanguagePack)),
                Add(volumes, 2, Package(1, Basic)),
                Add(volumes, 3, Package(2, Older)),
                Add(volumes, 4, Package(0, Target)),
                Cleanup(volumes, 5),
            ],
            _run.ToolRunner.Calls);
        Assert.Equal(
            ["#addingPackage de-DE language pack", "#addingPackage de-DE language pack", "#addingPackage 2026-07 update", "#addingPackage 2026-11 update", "#cleaningUp"],
            _run.Details.Values);
        Assert.Equal(100, _run.Progress.Values[^1]);
        Assert.False(System.IO.Directory.Exists(Path.Combine(Directory, "packages", s_step.Id.ToString("D"))));
        Assert.False(System.IO.Directory.Exists(Path.Combine(Directory, "scratch")));
        Assert.Empty(System.IO.Directory.EnumerateFiles(Directory, "dism-*.log"));
        Assert.Contains("Added de-DE language pack; de-DE language pack; 2026-07 update; 2026-11 update.", await MessagesAsync());
    }

    [Fact]
    public async Task WithoutCleanupNoCleanupRuns()
    {
        _run.Partitioned();

        await RunAsync(s_step with { Cleanup = false });

        Assert.DoesNotContain(_run.ToolRunner.Calls, call => call.Contains("/Cleanup-Image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMatchedUpdateThatDoesNotFitIsLeftOut()
    {
        _run.Partitioned();
        _run.ToolRunner.AnswerExitCode = (_, arguments, _) => NotApplicableFor(arguments, Older);

        StepResult result = await RunAsync(s_step);

        Assert.Equal(StepResult.Done(), result);
        Assert.Contains("Left out, as they don't fit this Windows: 2026-07 update.", await MessagesAsync());
    }

    [Fact]
    public async Task AListedPackageThatDoesNotFitFailsTheStep()
    {
        _run.Partitioned();
        _run.ToolRunner.AnswerExitCode = (_, arguments, _) => NotApplicableFor(arguments, Target);

        StepResult result = await RunAsync(s_step);

        Assert.Equal(StepResult.Failed(ServicePackagesStepRunner.DoesNotFit("2026-11 update")), result);
        Assert.DoesNotContain(_run.ToolRunner.Calls, call => call.Contains("/Cleanup-Image", StringComparison.Ordinal));
        Assert.False(System.IO.Directory.Exists(Path.Combine(Directory, "scratch")));
    }

    [Fact]
    public async Task AnUpdateNeedsThreeAndAHalfTimesItsSizeFree()
    {
        _run.Partitioned();
        long size = "checkpoint".Length + "target".Length;
        _space.Available = (long)(size * ServicePackagesStepRunner.UpdateSpaceFactor) - 1;

        StepResult result = await RunAsync(s_step);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal("Adding 2026-11 update needs 56 bytes free on the Windows partition, 3.5 times its 16 bytes, but it has 55 bytes.", result.Error);
        Assert.DoesNotContain(_run.ToolRunner.Calls, call => call.Contains(Target, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnyOtherExitCodeFailsWithTheEndOfDismsLog()
    {
        _run.Partitioned();
        _run.ToolRunner.AnswerExitCode = (_, arguments, _) =>
        {
            string log = arguments.Single(argument => argument.StartsWith("/LogPath:", StringComparison.Ordinal))["/LogPath:".Length..];
            File.WriteAllLines(log, Enumerable.Range(1, 50).Select(line => string.Create(CultureInfo.InvariantCulture, $"line {line}")));

            return unchecked((int)0x80070070);
        };

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => RunAsync(s_step));

        Assert.Equal("DISM could not add the packages (exit code 0x80070070). The last lines of its log are in the machine log.", exception.Message);
        List<string> messages = await MessagesAsync();
        Assert.Contains("dism.log: line 50", messages);
        Assert.Contains("dism.log: line 11", messages);
        Assert.DoesNotContain("dism.log: line 10", messages);
        Assert.Empty(System.IO.Directory.EnumerateFiles(Directory, "dism-*.log"));
    }

    [Fact]
    public async Task AFailedCleanupIsOnlyAWarning()
    {
        _run.Partitioned();
        _run.ToolRunner.AnswerExitCode = (_, arguments, _) => arguments.Contains("/Cleanup-Image") ? 5 : 3010;

        StepResult result = await RunAsync(s_step);

        Assert.Equal(StepResult.Done(), result);
        Assert.Contains(
            "DISM could not clean up the image (exit code 0x00000005). The last lines of its log are in the machine log. The packages are added; the image keeps the components they replaced.",
            await MessagesAsync());
    }

    [Fact]
    public async Task WithoutPackagesNothingRuns()
    {
        using StepRunnerFixture run = new([s_step]);

        StepResult result = await new ServicePackagesStepRunner(new DismOfflineServicing(run.ToolRunner, Dism, run.Log), run.Downloads, run.Session, run.Log)
            .RunAsync(s_step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Done(), result);
        Assert.Empty(run.ToolRunner.Calls);
    }

    public void Dispose() => _run.Dispose();

    private static int NotApplicableFor(IReadOnlyList<string> arguments, string file) =>
        arguments.Any(argument => argument.EndsWith(file, StringComparison.Ordinal)) ? DismOfflineServicing.NotApplicable : 0;

    private Task<StepResult> RunAsync(ServicePackagesStep step) => Runner.RunAsync(step, _run.Context(), TestContext.Current.CancellationToken);

    private async Task<List<string>> MessagesAsync() => [.. (await _run.SentLinesAsync()).Select(line => line.Message)];

    private string Package(int index, string file) =>
        Path.Combine(Directory, "packages", s_step.Id.ToString("D"), index.ToString(CultureInfo.InvariantCulture), file);

    private string Add(TargetVolumes volumes, int log, string file) => RecordingToolRunner.CommandLine(
        Dism,
        [$"/Image:{volumes.Windows}", "/Add-Package", $"/PackagePath:{file}", .. Common(log)]);

    private string Cleanup(TargetVolumes volumes, int log) => RecordingToolRunner.CommandLine(
        Dism,
        [$"/Image:{volumes.Windows}", "/Cleanup-Image", "/StartComponentCleanup", .. Common(log)]);

    private string[] Common(int log) =>
    [
        $"/ScratchDir:{Path.Combine(Directory, "scratch")}",
        $"/LogPath:{Path.Combine(Directory, string.Create(CultureInfo.InvariantCulture, $"dism-{log}.log"))}",
        "/LogLevel:2",
        "/English",
    ];
}
