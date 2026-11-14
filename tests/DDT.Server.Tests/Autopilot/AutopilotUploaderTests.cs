// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Autopilot;
using DDT.Server.Records;
using DDT.Server.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DDT.Server.Tests.Autopilot.AutopilotLab;

namespace DDT.Server.Tests.Autopilot;

// The uploader against a fake Autopilot. No test reaches Microsoft.
public sealed class AutopilotUploaderTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string ImportId = "985b4f49-4f49-985b-494f-5b98494f5b98";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private FakeAutopilotDirectory Autopilot => application.Autopilot;

    private AutopilotUploader Uploader(TimeProvider? time = null) => new(
        application.Services.GetRequiredService<IServiceScopeFactory>(),
        application.Services.GetRequiredService<DdtSettings>(),
        Autopilot,
        time ?? TimeProvider.System,
        NullLogger<AutopilotUploader>.Instance);

    private async Task<Guid> PendingAsync(string defaultGroupTag = "")
    {
        await SetUpAsync(application, defaultGroupTag);
        await SettleEarlierRowsAsync(application);

        return await MachineWithHashAsync(application);
    }

    [Fact]
    public async Task UploadsAPendingHashAndFollowsItUntilItIsImported()
    {
        Guid machineId = await PendingAsync("Default");
        Autopilot.Then(ImportId, AutopilotImportStatus.Running).Then(ImportId, AutopilotImportStatus.Running).Then(ImportId, AutopilotImportStatus.Complete);
        AutopilotUploader uploader = Uploader();

        Assert.Equal(HardwareHashUploadState.Pending, (await RowAsync(application, machineId)).UploadState);
        Assert.Equal(1, await uploader.UploadOnceAsync(Cancellation));
        Assert.Equal(0, await uploader.UploadOnceAsync(Cancellation));
        Assert.Equal(1, await uploader.UploadOnceAsync(Cancellation));

        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Imported, ImportId, (string?)null), (row.UploadState, row.ImportId, row.UploadMessage));
        Assert.Equal(["import SN-1", $"read {ImportId}", $"read {ImportId}"], Autopilot.Calls);
        Assert.Equal(new AutopilotDevice("SN-1", Hash, "Default"), Assert.Single(Autopilot.Devices));
    }

    [Fact]
    public async Task AFailedImportKeepsAutopilotsWords()
    {
        Guid machineId = await PendingAsync();
        Autopilot.Then(ImportId, AutopilotImportStatus.Running).Then(ImportId, AutopilotImportStatus.Error, "ZtdDeviceAlreadyAssigned (806)");
        AutopilotUploader uploader = Uploader();

        await uploader.UploadOnceAsync(Cancellation);
        await uploader.UploadOnceAsync(Cancellation);

        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Failed, "ZtdDeviceAlreadyAssigned (806)"), (row.UploadState, row.UploadMessage));
    }

    [Fact]
    public async Task ARefusalFailsTheUpload()
    {
        Guid machineId = await PendingAsync();
        Autopilot.Then(() => throw new AutopilotDirectoryException("Microsoft Graph refused the request (403): Forbidden."));

        await Uploader().UploadOnceAsync(Cancellation);

        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Failed, "Microsoft Graph refused the request (403): Forbidden."), (row.UploadState, row.UploadMessage));
    }

    [Fact]
    public async Task AnUnreachableServiceLeavesTheHashPendingForTheNextPass()
    {
        Guid machineId = await PendingAsync();
        Autopilot.Then(() => throw new HttpRequestException("No such host is known."));

        Assert.Equal(0, await Uploader().UploadOnceAsync(Cancellation));

        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Pending, (string?)null), (row.UploadState, row.ImportId));
    }

    [Fact]
    public async Task GivesUpOnAnImportAfterHalfAnHour()
    {
        Guid machineId = await PendingAsync();
        ManualTimeProvider time = new();
        Autopilot.Then(ImportId, AutopilotImportStatus.Running).Then(ImportId, AutopilotImportStatus.Running);
        AutopilotUploader uploader = Uploader(time);

        await uploader.UploadOnceAsync(Cancellation);
        time.Advance(AutopilotUploader.GiveUpAfter);
        await uploader.UploadOnceAsync(Cancellation);
        time.Advance(TimeSpan.FromSeconds(30));
        await uploader.UploadOnceAsync(Cancellation);

        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Failed, AutopilotUploader.TookTooLong), (row.UploadState, row.UploadMessage));
        Assert.Equal(["import SN-1", $"read {ImportId}"], Autopilot.Calls);
    }

    [Fact]
    public async Task WithoutCompleteSettingsNothingIsPendingOrUploaded()
    {
        await TakeDownAsync(application);
        await SettleEarlierRowsAsync(application);
        Guid machineId = await MachineWithHashAsync(application);

        Assert.Equal(0, await Uploader().UploadOnceAsync(Cancellation));

        Assert.Equal(HardwareHashUploadState.None, (await RowAsync(application, machineId)).UploadState);
        Assert.Empty(Autopilot.Calls);
    }

    [Fact]
    public async Task TheStepsGroupTagWinsOverTheDefault()
    {
        await SetUpAsync(application, "Default");
        await SettleEarlierRowsAsync(application);

        Guid machineId = await MachineWithHashAsync(application, groupTag: "Sales");

        Assert.Equal("Sales", (await RowAsync(application, machineId)).GroupTag);
    }
}
