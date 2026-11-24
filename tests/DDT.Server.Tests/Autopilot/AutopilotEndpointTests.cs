// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Records;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.Autopilot.AutopilotLab;

namespace DDT.Server.Tests.Autopilot;

// Intune's CSV for one machine or a selection, and an upload asked for again on the machine's page.
public sealed class AutopilotEndpointTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneMachinesCsvIsWhatIntuneImports()
    {
        Guid machineId = await MachineWithHashAsync(application, "SN,1\"", "Sales");
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);

        HttpResponseMessage response = await operatorClient.GetAsync($"/api/machines/{machineId}/autopilot.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("autopilot.csv", response.Content.Headers.ContentDisposition?.FileName);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(Cancellation);
        Assert.All(bytes, b => Assert.True(b < 0x80));
        Assert.Equal(
            $"Device Serial Number,Windows Product ID,Hardware Hash,Group Tag,Assigned User\r\nSN1,,{Hash},Sales,\r\n",
            Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task ASelectionLeavesOutMachinesWithoutAHash()
    {
        Guid first = await MachineWithHashAsync(application, "B-2");
        Guid second = await MachineWithHashAsync(application, "A-1");
        Guid none = (await application.RegisterMachineAsync()).Id;
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);

        HttpResponseMessage response = await operatorClient.PostAsync("/api/machines/autopilot.csv", new AutopilotCsvRequest([first, none, second]));

        string[] lines = (await response.Content.ReadAsStringAsync(Cancellation)).Split("\r\n");
        Assert.Equal(["A-1", "B-2"], lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')[0]));
    }

    [Fact]
    public async Task RefusesMoreThanIntuneTakesAndNoneWithAHash()
    {
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);

        HttpResponseMessage tooMany = await operatorClient.PostAsync(
            "/api/machines/autopilot.csv",
            new AutopilotCsvRequest([.. Enumerable.Range(0, 501).Select(_ => Guid.NewGuid())]));
        HttpResponseMessage none = await operatorClient.PostAsync("/api/machines/autopilot.csv", new AutopilotCsvRequest([Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Contains(ServerMessages.AutopilotTooMany.Code, await tooMany.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
    }

    [Fact]
    public async Task AViewerExportsNothing()
    {
        Guid machineId = await MachineWithHashAsync(application);
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/machines/{machineId}/autopilot.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/machines/{machineId}/autopilot/upload")).StatusCode);
    }

    [Fact]
    public async Task AnUploadAskedForAgainWaitsForTheUploader()
    {
        await TakeDownAsync(application);
        Guid machineId = await MachineWithHashAsync(application);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);

        HttpResponseMessage notSetUp = await operatorClient.PostAsync($"/api/machines/{machineId}/autopilot/upload");
        await SetUpAsync(application);
        await application.QueryAsync(database => database.MachineHardwareHashes
            .Where(h => h.MachineId == machineId)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.UploadState, HardwareHashUploadState.Failed).SetProperty(h => h.UploadMessage, "Earlier"), Cancellation));
        HttpResponseMessage again = await operatorClient.PostAsync($"/api/machines/{machineId}/autopilot/upload");
        HttpResponseMessage noHash = await operatorClient.PostAsync($"/api/machines/{(await application.RegisterMachineAsync()).Id}/autopilot/upload");

        Assert.Equal(HttpStatusCode.Conflict, notSetUp.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noHash.StatusCode);
        MachineHardwareHash row = await RowAsync(application, machineId);
        Assert.Equal((HardwareHashUploadState.Pending, (string?)null), (row.UploadState, row.UploadMessage));
        await TakeDownAsync(application);
    }
}
