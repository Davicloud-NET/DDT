// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class InjectDriversStepRunnerTests
{
    private static readonly InjectDriversStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a3"),
        Name = "Drivers",
    };

    private readonly TestZip _network = new(("net/e1000.inf", "[Version]"), ("net/e1000.sys", "driver"));
    private readonly TestZip _storage = new(("storage/", string.Empty), ("storage/nvme.inf", "[Version]"));

    [Fact]
    public async Task AddsEachPackageToTheOfflineWindowsWithDism()
    {
        using StepRunnerFixture run = Run(_network, _storage);
        TargetVolumes volumes = run.Partitioned();
        string directory = Path.Combine(volumes.Windows, "DDT");
        string drivers = Path.Combine(directory, "packages", s_step.Id.ToString("D"));
        List<string> unpacked = [];
        run.ToolRunner.Answer = (_, arguments) =>
        {
            string driver = arguments.Single(argument => argument.StartsWith("/Driver:", StringComparison.Ordinal))["/Driver:".Length..];
            unpacked.AddRange(Directory.EnumerateFiles(driver, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(driver, file)));

            return [];
        };

        StepResult result = await run.InjectDrivers.RunAsync(s_step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal([Dism(volumes, drivers, 0), Dism(volumes, drivers, 1)], run.ToolRunner.Calls);
        Assert.Equal([Path.Combine("net", "e1000.inf"), Path.Combine("net", "e1000.sys"), Path.Combine("storage", "nvme.inf")], unpacked);
        Assert.Equal(100, run.Progress.Values[^1]);

        // Only DISM's log directory stays.
        Assert.False(Directory.Exists(drivers));
        Assert.False(Directory.Exists(Path.Combine(directory, "scratch")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory, "cache")));
        Assert.True(Directory.Exists(Path.Combine(directory, "logs")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutAPackageForTheModelNoDriversAreAdded(bool requireMatch)
    {
        using StepRunnerFixture run = Run();
        run.Partitioned();

        StepResult result = await run.InjectDrivers.RunAsync(s_step with { RequireMatch = requireMatch }, run.Context(), TestContext.Current.CancellationToken);

        if (requireMatch)
        {
            Assert.Equal(StepResult.Failed("The server has no driver package for this model (Latitude 5440), and this step requires one."), result);
        }
        else
        {
            Assert.Equal(StepOutcome.Done, result.Outcome);
        }

        Assert.Empty(run.ToolRunner.Calls);
        Assert.Empty(run.Server.Calls);
    }

    [Fact]
    public async Task APackageThatWouldWriteOutsideItsDirectoryAddsNothing()
    {
        TestZip evil = new(("../../../Windows/System32/evil.inf", "[Version]"));
        using StepRunnerFixture run = Run(evil);
        TargetVolumes volumes = run.Partitioned();

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => run.InjectDrivers.RunAsync(s_step, run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal("The package Latitude drivers holds an entry named ../../../Windows/System32/evil.inf, which the agent does not unpack.", exception.Message);
        Assert.Empty(run.ToolRunner.Calls);
        Assert.Empty(Directory.EnumerateFiles(volumes.Windows, "evil.inf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AFailedDismFailsTheStepAndCleansUp()
    {
        using StepRunnerFixture run = Run(_network);
        TargetVolumes volumes = run.Partitioned();
        run.ToolRunner.Answer = (_, _) => throw new DeploymentStepException("dism.exe failed with exit code 0x00000002. Its output is in the machine log.");

        await Assert.ThrowsAsync<DeploymentStepException>(() => run.InjectDrivers.RunAsync(s_step, run.Context(), TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(Path.Combine(volumes.Windows, "DDT", "packages", s_step.Id.ToString("D"))));
        Assert.False(Directory.Exists(Path.Combine(volumes.Windows, "DDT", "scratch")));
    }

    private static string Dism(TargetVolumes volumes, string drivers, int index)
    {
        string directory = Path.Combine(volumes.Windows, "DDT");

        return RecordingToolRunner.CommandLine(
            InjectDriversStepRunner.DismPath,
            $"/Image:{volumes.Windows}",
            "/Add-Driver",
            $"/Driver:{Path.Combine(drivers, index.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
            "/Recurse",
            $"/ScratchDir:{Path.Combine(directory, "scratch")}",
            $"/LogPath:{Path.Combine(directory, "logs", $"dism-{s_step.Id:D}.log")}");
    }

    private static StepRunnerFixture Run(params TestZip[] packages)
    {
        StepRunnerFixture run = new(
            [s_step],
            packages: [.. packages.Select((package, index) => new AgentRunPackage(s_step.Id, index == 0 ? "Latitude drivers" : "Storage drivers", package.Sha256, package.Content.Length))]);

        foreach (TestZip package in packages)
        {
            run.Server.ServeFile(package.Sha256, package.Content);
        }

        return run;
    }
}
