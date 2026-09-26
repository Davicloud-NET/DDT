// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WriteRawImageStepRunnerTests : IDisposable
{
    private static readonly WriteRawImageStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private readonly TestRawImage _image = new();
    private StepRunnerFixture _run;

    public WriteRawImageStepRunnerTests()
    {
        _run = Fixture(ImageBootCapability.SecureBootOk, secureBootEnabled: true);
    }

    public void Dispose() => _run.Dispose();

    private StepRunnerFixture Fixture(ImageBootCapability capability, bool? secureBootEnabled, bool allowed = false)
    {
        StepRunnerFixture run = new([s_step], [_image.RunImage(capability)], secureBootEnabled: secureBootEnabled, allowSecureBootMismatch: allowed);
        _image.Serve(run.Server);

        return run;
    }

    private void Use(StepRunnerFixture run)
    {
        _run.Dispose();
        _run = run;
    }

    private Task<StepResult> RunAsync() => _run.WriteRawImage.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task CleansTheDiskAndWritesTheImageAsItDownloads()
    {
        StepResult result = await RunAsync();

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(["clean 0"], _run.Tools.Calls);
        Assert.Equal([$"open-file {_image.Sha256} 0 session"], _run.Server.Calls);
        Assert.Equal(100, _run.Progress.Values[^1]);

        MemoryRawDisk disk = _run.RawDisks.Disks[0];
        GptLayout written = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes));
        Assert.Equal(_image.Layout.Partitions, written.Partitions);
        Assert.Equal(disk.Length / GptLayout.SectorSize - 1, written.BackupLba);
        Assert.True(disk.Disposed);

        IReadOnlyDictionary<string, string> outputs = result.Outputs!;
        Assert.Equal(RunVariables.Set, outputs[RunVariables.RawImageWritten]);
        Assert.Equal(
            new EspPartition(TestRawImage.EspNumber, TestRawImage.EspFirst, TestRawImage.EspSectors, TestRawImage.EspId),
            RunVariables.RawSystemPartitionOf(outputs));
        Assert.Equal([FakeDeploymentTools.ErasedSystemPartitionId], RunVariables.ErasedSystemPartitionIdsOf(outputs));
    }

    [Theory]
    [InlineData(ImageBootCapability.NotSigned, "noble-test is not signed for Secure Boot, and this machine has Secure Boot on")]
    [InlineData(ImageBootCapability.Unknown, "noble-test may not start with Secure Boot on, as DDT could not tell whether it is signed for it, and this")]
    public async Task RefusesAnImageTheFirmwareWouldNotStartBeforeErasingAnything(ImageBootCapability capability, string reason)
    {
        Use(Fixture(capability, secureBootEnabled: true));

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(RunAsync);

        Assert.StartsWith(reason, refusal.Message, StringComparison.Ordinal);
        Assert.EndsWith("Turn Secure Boot off in the firmware setup, or start the run again allowing the image.", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(_run.Tools.Calls);
        Assert.Empty(_run.RawDisks.Disks);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task WritesAnUnsignedImageWhereAllowedOrWhereSecureBootIsNotOn(bool? secureBootEnabled, bool allowed)
    {
        Use(Fixture(ImageBootCapability.NotSigned, secureBootEnabled, allowed));

        Assert.Equal(StepOutcome.Done, (await RunAsync()).Outcome);
        Assert.Equal(["clean 0"], _run.Tools.Calls);
    }

    // A firmware with only the 2011 CA does not start a shim signed since June 2026, under the 2023 one.
    [Fact]
    public async Task RefusesASignedImageWhereTheFirmwareDoesNotTrustItsCa()
    {
        AgentRunImage image2023 = _image.RunImage(signedUnder: UefiCa.Microsoft2023);
        Use(new StepRunnerFixture([s_step], [image2023], secureBootEnabled: true, trustedUefiCas: UefiCa.Microsoft2011));
        _image.Serve(_run.Server);

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(RunAsync);

        Assert.Equal(
            "noble-test is signed under Microsoft's third-party UEFI CA 2023, which this machine's firmware does not trust, and this " +
            "machine has Secure Boot on, so it would not start. Allow that CA or turn Secure Boot off in the firmware setup, or start the " +
            "run again allowing the image.",
            refusal.Message);
        Assert.Empty(_run.Tools.Calls);

        Use(new StepRunnerFixture([s_step], [image2023], secureBootEnabled: true, allowSecureBootMismatch: true, trustedUefiCas: UefiCa.Microsoft2011));
        _image.Serve(_run.Server);
        Assert.Equal(StepOutcome.Done, (await RunAsync()).Outcome);

        Use(new StepRunnerFixture([s_step], [image2023], secureBootEnabled: true, trustedUefiCas: UefiCa.Microsoft2011 | UefiCa.Microsoft2023));
        _image.Serve(_run.Server);
        Assert.Equal(StepOutcome.Done, (await RunAsync()).Outcome);
    }

    [Fact]
    public async Task RefusesADiskWithSectorsOfFourKilobytes()
    {
        _run.RawDisks.SectorSize = 4096;

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(RunAsync);

        Assert.Equal($"Disk 0 {WriteRawImageStepRunner.FourKilobyteSectorsMessage}", refusal.Message);
    }

    [Fact]
    public async Task FailsWhenTheDownloadDoesNotMatchItsHash()
    {
        byte[] other = new TestRawImage(seed: 9).Compressed;
        Use(new StepRunnerFixture([s_step], [_image.RunImage() with { SizeBytes = other.Length }]));
        _run.Server.ServeFile(_image.Sha256, other);

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(RunAsync);

        Assert.StartsWith("The download of noble-test does not match the SHA-256", refusal.Message, StringComparison.Ordinal);
    }
}
