// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The checks before a fresh run changes anything, so nothing is erased for a run that cannot succeed.
public sealed class SequenceRunnerPreflightTests : SequenceRunnerTestBase
{
    // What LargeRun needs, in MB: 1340 for the partitions, 4608 for the image's download and 20480 for its files, the
    // package's 1024 twice, and 2048 to spare.
    private const long LargeRunBytes = 30524L * 1024 * 1024;

    public static TheoryData<string> PreflightFailures =>
    [
        "invalid sequence",
        "no image for the step",
        "no DISM",
        "no PowerShell",
        "no disk",
        "several disks",
        "chosen before the agent started",
        "chosen disk gone",
        "another disk under the chosen number",
        "disk too small",
        "wimlib",
        "image missing",
        "image size",
    ];

    [Theory]
    [MemberData(nameof(PreflightFailures))]
    public async Task APreflightFailureReportsFailedWithTheDiskUntouched(string failure)
    {
        ScriptedAgentServer server = new ScriptedAgentServer().ServeFile(_image.Sha256, _image.Content);
        PreflightFailure arranged = SequenceFailure(failure) ?? DiskFailure(failure) ?? ServerFailure(failure, server);

        RunResult result = await RunAsync(server, arranged.Run, new() { ConfirmedDisk = arranged.ConfirmedDisk });

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.StartsWith(arranged.Expected, report.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErasesADiskThatHoldsExactlyWhatTheRunNeeds()
    {
        _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: LargeRunBytes);
        AgentRun run = LargeRun();
        ScriptedAgentServer server = new ScriptedAgentServer().OnHeadRunFile(_ => run.Images[0].SizeBytes);

        await RunAsync(server, run);

        Assert.Contains("partition 0", _tools.Calls);
    }

    // A run only finds out which branch it takes as it goes. So the image of every branch is checked before anything is
    // erased, and only the branch taken applies its image.
    [Fact]
    public async Task ChecksTheImageOfEveryBranchBeforeErasing()
    {
        TestImage enterprise = new();
        AgentRun run = ImagePerModel(enterprise, 10_000);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer()).ServeFile(enterprise.Sha256, enterprise.Content);

        RunResult result = await RunAsync(server, run);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal([$"head-file {_image.Sha256} session-0", $"head-file {enterprise.Sha256} session-0", "run-report Running session-0"], server.Calls.Take(3));
        Assert.Single(_tools.Calls, call => call.StartsWith("apply", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Calls, call => call.StartsWith($"open-file {enterprise.Sha256}", StringComparison.Ordinal));
    }

    // The disk has to hold the path that needs the most, whichever the machine takes.
    [Fact]
    public async Task ADiskTooSmallForAnyBranchIsNotErased()
    {
        TestImage enterprise = new();
        AgentRun run = ImagePerModel(enterprise, 300L * 1024 * 1024 * 1024);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer()).ServeFile(enterprise.Sha256, enterprise.Content);

        RunResult result = await RunAsync(server, run);

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        string? error = Assert.Single(server.RunReports).Error;
        Assert.StartsWith("Disk 0 holds 256 GB, but Install Windows needs 303 GB on the path through it that needs the most, 2 GB to spare included.", error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    // An IF on the model applies this build's test image on the dry run's machine, and another image elsewhere.
    private AgentRun ImagePerModel(TestImage other, long otherInstalledBytes)
    {
        ApplyImageStep enterprise = TestRuns.Apply with { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b012"), ImageId = Guid.Parse("0193a4b2-0000-7000-8000-00000000a002") };
        IfStep byModel = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b011"),
            Name = "Image by model",
            Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, s_identity.Model!),
            Then = [TestRuns.Apply],
            Else = [enterprise],
        };

        return TestRuns.Run([TestRuns.Partition, byModel, TestRuns.Unattend], _image) with
        {
            Images = [_image.RunImage, other.RunImage with { ImageId = enterprise.ImageId, Name = "Windows 11 Enterprise", InstalledBytes = otherInstalledBytes }],
        };
    }

    private AgentRun LargeRun() => InstallWindows() with
    {
        Images = [_image.RunImage with { SizeBytes = 4608L * 1024 * 1024, InstalledBytes = 20L * 1024 * 1024 * 1024 }],
        Packages = [new AgentRunPackage(s_drivers.Id, "Drivers", _image.Sha256, 1024L * 1024 * 1024)],
    };

    // A sequence this agent cannot run, or a boot image without its tools.
    private PreflightFailure? SequenceFailure(string failure)
    {
        switch (failure)
        {
            case "invalid sequence":
                return new(TestRuns.Run([TestRuns.Apply, TestRuns.Partition], _image), "Install Windows cannot run: ");
            case "no image for the step":
                return new(InstallWindows() with { Images = [] }, "The server sent no image for step Apply image. Assign the sequence again.");
            case "no DISM":
                File.Delete(InjectDriversStepRunner.DismIn(_system));

                return new(TestRuns.Run([TestRuns.Partition, TestRuns.Apply, s_drivers, TestRuns.Unattend], _image), InjectDriversStepRunner.NoDismMessage);
            case "no PowerShell":
                File.Delete(RunScriptStepRunner.PowerShellIn(_system));

                return new(
                    TestRuns.Run([TestRuns.Partition, TestRuns.Script(1, interpreter: ScriptInterpreter.PowerShell), TestRuns.Apply, TestRuns.Unattend], _image),
                    RunScriptStepRunner.NoPowerShellMessage);
            default:
                return null;
        }
    }

    // No disk to erase, or not the one chosen at the machine, or one too small.
    private PreflightFailure? DiskFailure(string failure)
    {
        switch (failure)
        {
            case "no disk":
                _tools.Disks.Clear();

                return new(InstallWindows(), SequenceRunner.NoDiskMessage);
            case "several disks":
                _tools.Disks.Add(FakeDeploymentTools.Disk(1));

                return new(InstallWindows(), SequenceRunner.SeveralDisksMessage);
            case "chosen before the agent started":
                return new(InstallWindows(diskNumber: 0), "Disk 0 was chosen before the agent started again, and disk numbers can change when a machine restarts");
            case "chosen disk gone":
                return new(InstallWindows(diskNumber: 5), "Disk 5, chosen at this machine, is not a disk DDT can install on now.", FakeDeploymentTools.Disk(5));
            case "another disk under the chosen number":
                return new(
                    InstallWindows(diskNumber: 0),
                    "Disk 0 is no longer the disk chosen at this machine (USB stick, 256 GB)",
                    FakeDeploymentTools.Disk(0) with { Model = "USB stick", BusType = StorageBusType.Usb });
            case "disk too small":
                // A byte short, so leaving out any part, even the reserved partition, would let the run through.
                _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: LargeRunBytes - 1);

                return new(
                    LargeRun(),
                    "Disk 0 holds 30 GB, but Install Windows needs 30 GB: 1.3 GB for the boot and recovery partitions, 4.5 GB for the " +
                        "image downloads, 20 GB for the installed files, 2 GB for the packages and their contents and 2 GB to spare. Run it on a larger disk.");
            default:
                return null;
        }
    }

    // wimlib that can't be used, or an image the server doesn't have in the form the run expects.
    private PreflightFailure ServerFailure(string failure, ScriptedAgentServer server)
    {
        switch (failure)
        {
            case "wimlib":
                _tools.FailAt = "prepare";
                _tools.Failure = new DeploymentStepException("wimlib cannot be used.");

                return new(InstallWindows(), "wimlib cannot be used.");
            case "image missing":
                server.OnHeadRunFile(_ => throw new AgentRequestException("404", "The image file is missing on the server.", HttpStatusCode.NotFound));

                return new(InstallWindows(), "The image file is missing on the server.");
            default:
                server.OnHeadRunFile(_ => 12);

                return new(InstallWindows(), "The server's file for Windows 11 Pro holds 12 bytes instead of 3000. Upload the image again.");
        }
    }

    // What makes a run fail its checks, and how its error starts.
    private sealed record PreflightFailure(AgentRun Run, string Expected, LocalDisk? ConfirmedDisk = null);
}
