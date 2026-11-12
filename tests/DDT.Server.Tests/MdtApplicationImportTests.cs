// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Applications;
using DDT.Contracts.Import;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.Import;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;
using static DDT.Server.Tests.MdtApplicationShares;

namespace DDT.Server.Tests;

// An MDT share's applications and bundles become the library's, with their files, dependencies and restarts.
public sealed class MdtApplicationImportTests : IDisposable
{
    private const string Mdt = "/api/images/import/mdt";

    private readonly ImageImportApplication _application = new();

    [Fact]
    public async Task TheShareListsItsApplicationsAndWhatTheLibraryHasAlready()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);
        string share = Write(_application.SharePath);
        await administrator.CreatedApplicationAsync(Command("viewer"));

        MdtShareView view = await RegisteredMachine.ReadAsync<MdtShareView>(await administrator.PostAsync($"{Mdt}/inspect", new InspectMdtShareRequest(share)));

        Assert.Equal(11, view.Applications.Count);
        Assert.Equal(
            new MdtApplication(SevenZip, "7-Zip 24.09", "24.09", "Igor Pavlov", true, ApplicationReferenceKind.Application, false),
            view.Applications[0]);
        Assert.Equal(["Viewer"], view.Applications.Where(application => application.InLibrary).Select(application => application.Name));
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(Mdt, Request(share, Viewer))).StatusCode);
    }

    [Fact]
    public async Task AnApplicationWithASourceFolderGetsItsFilesAndItsUninstallKeyEvenAGuid()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        ImportStatus job = await ImportAsync(administrator, SevenZip);

        Assert.Equal(new ImportResult("7-Zip 24.09", ImportOutcome.Added, Notes: []), job.Results.Single(), ResultComparer.Instance);
        ApplicationView sevenZip = Assert.Single(await ApplicationsAsync(administrator));
        Assert.Equal(
            (ApplicationKind.Command, "msiexec /i 7z.msi /qn", "Packs files.", "Igor Pavlov", "24.09"),
            (sevenZip.Kind, sevenZip.Command, sevenZip.Description, sevenZip.Publisher, sevenZip.Version));
        Assert.Equal(new UninstallKeyDetection(ProductCode), sevenZip.Detection);
        Assert.Equal([0], sevenZip.SuccessExitCodes);
        Assert.Equal([3010], sevenZip.RestartExitCodes);
        Guid packageId = sevenZip.Files?.PackageId ?? Guid.Empty;
        Package package = await _application.QueryAsync(database => database.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId, TestContext.Current.CancellationToken));
        Assert.Equal((PackageKind.Application, 2), (package.Kind, package.FileCount));
    }

    [Fact]
    public async Task AnApplicationWithoutFilesRestartsWhereMdtDidAndDetectsItsUninstallKey()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        await ImportAsync(administrator, Runtime);

        ApplicationView runtime = Assert.Single(await ApplicationsAsync(administrator));
        Assert.Null(runtime.Files);
        Assert.Equal(@"\\files\apps\vc_redist.x64.exe /install /quiet", runtime.Command);
        Assert.Equal(new UninstallKeyDetection("VCRuntime"), runtime.Detection);
        Assert.Empty(runtime.SuccessExitCodes);
        Assert.Equal([0, 3010], runtime.RestartExitCodes);
    }

    [Fact]
    public async Task ADependencyOnABundleBecomesItsApplicationsAndWhatIsLostIsNoted()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        ImportStatus job = await ImportAsync(administrator, Notepad, SevenZip, Runtime);

        List<ApplicationView> applications = await ApplicationsAsync(administrator);
        ApplicationView notepad = Assert.Single(applications, application => application.Name == "Notepad++, portable");
        Assert.Equal([IdOf(applications, "VC++ Runtime"), IdOf(applications, "7-Zip 24.09")], notepad.DependsOn);
        Assert.Equal(["7-Zip 24.09", "VC++ Runtime", "Notepad++, portable"], job.Results.Select(result => result.Name));
        Assert.Equal(
            [("import.dependencyMissing", Missing), ("import.workingDirectory", @".\Applications\Other")],
            (job.Results[2].Notes ?? []).Select(note => (note.Code, note.Args.Values.Single().ToString())));
    }

    [Fact]
    public async Task ACircleInTheFileIsCutAndNoted()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        ImportStatus job = await ImportAsync(administrator, CycleOne, CycleTwo);

        Assert.Equal(["Cycle Two", "Cycle One"], job.Results.Select(result => result.Name));
        Assert.Equal("import.dependencyCycle", Assert.Single(job.Results[0].Notes ?? []).Code);
        List<ApplicationView> applications = await ApplicationsAsync(administrator);
        Assert.Empty(Assert.Single(applications, application => application.Name == "Cycle Two").DependsOn);
        Assert.Equal([IdOf(applications, "Cycle Two")], Assert.Single(applications, application => application.Name == "Cycle One").DependsOn);
    }

    [Fact]
    public async Task BundlesGetTheirApplicationsInOrderAndAnEmptyOneIsNotCreated()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        ImportStatus job = await ImportAsync(administrator, BaseBundle, InnerBundle, EmptyBundle, SevenZip, Runtime);

        List<ApplicationView> applications = await ApplicationsAsync(administrator);
        List<ApplicationBundleView> bundles = await BundlesAsync(administrator);
        Guid sevenZip = IdOf(applications, "7-Zip 24.09");
        Guid runtime = IdOf(applications, "VC++ Runtime");
        Assert.Equal([sevenZip, runtime], Assert.Single(bundles, bundle => bundle.Name == "Base Bundle").Members);
        Assert.Equal("What every PC gets.", Assert.Single(bundles, bundle => bundle.Name == "Base Bundle").Description);
        Assert.Equal([runtime, sevenZip], Assert.Single(bundles, bundle => bundle.Name == "Inner Bundle").Members);
        Assert.Equal(2, bundles.Count);
        ImportResult empty = Assert.Single(job.Results, result => result.Name == "Empty Bundle");
        Assert.Equal((ImportOutcome.Refused, "import.bundleEmpty"), (empty.Outcome, empty.Reason?.Code));
    }

    [Fact]
    public async Task WhatTheRulesRefuseIsSkippedWithTheirReason()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        ImportStatus job = await ImportAsync(administrator, NoCommand);

        Assert.Equal((ImportOutcome.Refused, "application.commandRequired"), (job.Results[0].Outcome, job.Results[0].Reason?.Code));
        Assert.Empty(await ApplicationsAsync(administrator));
    }

    [Fact]
    public async Task ANameTheLibraryHasIsLeftAsItIsAndDependenciesLinkToIt()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        ApplicationView mine = await administrator.CreatedApplicationAsync(Command("7-ZIP  24.09"));

        ImportStatus job = await ImportAsync(administrator, SevenZip, Runtime, BaseBundle);

        Assert.Equal(ImportOutcome.Existing, job.Results[0].Outcome);
        Assert.Equal(mine.Command, (await administrator.ApplicationAsync(mine.Id)).Command);
        Assert.Equal(mine.Id, Assert.Single(await BundlesAsync(administrator)).Members[0]);
    }

    [Fact]
    public async Task ASecondImportAddsNothingAndLinksToWhatTheFirstAdded()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        await ImportAsync(administrator, Viewer, SevenZip);
        int packages = await _application.QueryAsync(database => database.Packages.CountAsync(TestContext.Current.CancellationToken));

        ImportStatus job = await ImportAsync(administrator, Viewer, UsesViewer, SevenZip);

        Assert.Equal([ImportOutcome.Existing, ImportOutcome.Existing, ImportOutcome.Added], job.Results.Select(result => result.Outcome));
        List<ApplicationView> applications = await ApplicationsAsync(administrator);
        Assert.Equal([IdOf(applications, "Viewer")], Assert.Single(applications, application => application.Name == "Uses Viewer").DependsOn);
        Assert.Equal(packages, await _application.QueryAsync(database => database.Packages.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ImportedEntriesAreAuditedAndPushed()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(_application, administrator);
        ChannelReader<ApplicationView> changed = live.Listen<ApplicationView>(LiveEvents.ApplicationChanged);
        ChannelReader<ApplicationBundleView> bundleChanged = live.Listen<ApplicationBundleView>(LiveEvents.ApplicationBundleChanged);

        ImportStatus job = await ImportAsync(administrator, Runtime, InnerBundle);

        ApplicationView runtime = await LiveListener.NextAsync(changed, view => view.Name == "VC++ Runtime");
        ApplicationBundleView inner = await LiveListener.NextAsync(bundleChanged, view => view.Name == "Inner Bundle");
        Assert.Equal([runtime.Id], inner.Members);
        Assert.Equal("import.dependencyMissing", Assert.Single(job.Results[1].Notes ?? []).Code);
        List<string> actions = await _application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == runtime.Id.ToString("D") || e.SubjectId == inner.Id.ToString("D"))
            .OrderBy(e => e.Id)
            .Select(e => e.Action)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal([AuditActions.ApplicationCreated, AuditActions.ApplicationBundleCreated], actions);
    }

    [Fact]
    public async Task OnlyOneImportRunsAtATime()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string share = Write(_application.SharePath);
        CurrentImport current = _application.Services.GetRequiredService<CurrentImport>();
        Assert.True(current.TryBegin("someone else", 1));

        HttpResponseMessage busy = await administrator.PostAsync(Mdt, Request(share, Viewer));

        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal("import.busy", await CodeAsync(busy));
        current.Finish();
        Assert.Empty(await ApplicationsAsync(administrator));
    }

    public void Dispose() => _application.Dispose();

    private static ImportMdtShareRequest Request(string share, params string[] applications) => new(share, [], [], applications);

    private async Task<ImportStatus> ImportAsync(SignedInClient administrator, params string[] applications)
    {
        string share = Write(_application.SharePath);
        Assert.Equal(HttpStatusCode.Accepted, (await administrator.PostAsync(Mdt, Request(share, applications))).StatusCode);
        await _application.Services.GetRequiredService<LibraryImports>().Last;

        ImportSources sources = await RegisteredMachine.ReadAsync<ImportSources>(await administrator.GetAsync("/api/images/import"));

        return Assert.IsType<ImportStatus>(sources.Job);
    }

    private static async Task<List<ApplicationView>> ApplicationsAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<List<ApplicationView>>(await client.GetAsync(ApplicationsPath));

    private static async Task<List<ApplicationBundleView>> BundlesAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<List<ApplicationBundleView>>(await client.GetAsync(BundlesPath));

    private static Guid IdOf(List<ApplicationView> applications, string name) => Assert.Single(applications, application => application.Name == name).Id;

    // A result's notes are a list, which records compare by reference.
    private sealed class ResultComparer : IEqualityComparer<ImportResult>
    {
        public static ResultComparer Instance { get; } = new();

        public bool Equals(ImportResult? x, ImportResult? y) =>
            x is not null && y is not null && (x.Name, x.Outcome, x.Reason?.Code) == (y.Name, y.Outcome, y.Reason?.Code)
            && (x.Notes ?? []).Select(note => note.Code).SequenceEqual((y.Notes ?? []).Select(note => note.Code));

        public int GetHashCode(ImportResult obj) => HashCode.Combine(obj.Name, obj.Outcome);
    }
}
