// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Applications;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Saving applications: the form's rules, then names unique across applications and bundles, files and dependencies that
// exist, and no dependency circle.
public sealed class ApplicationLibraryTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private Task<List<AuditEvent>> AuditAsync(Guid id)
    {
        string subject = id.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<IDictionary<string, string[]>> ProblemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors;
    }

    [Fact]
    public async Task CreatesAnApplicationWithItsFilesAndTheProductCodeInWindowsForm()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(TestMsi.Fixture(), $"seven-{Guid.NewGuid():N}.msi");
        string name = Unique("Seven");

        ApplicationView created = await administrator.CreatedApplicationAsync(Command($"  {name.Replace(" ", "   ", StringComparison.Ordinal)} ", upload.Files.PackageId) with
        {
            Detection = new ProductCodeDetection(" 6f1c2e5a-3b7d-4c9e-8a21-0d4f5b6c7e81 "),
            Publisher = " Davicloud ",
            Description = " ",
        });

        Assert.Equal(name, created.Name);
        Assert.Equal("Davicloud", created.Publisher);
        Assert.Null(created.Description);
        Assert.Equal(upload.Files, created.Files);
        Assert.Equal(new ProductCodeDetection(TestMsi.ProductCode), created.Detection);
        Assert.Equal(1, created.Revision);
        Assert.Empty(created.UsedBy);
        Assert.StartsWith("administrator-", created.UpdatedBy, StringComparison.Ordinal);
        Assert.Equal(created.Id, (await administrator.ApplicationAsync(created.Id)).Id);

        AuditEvent audit = Assert.Single(await AuditAsync(created.Id));
        Assert.Equal(AuditActions.ApplicationCreated, audit.Action);
        Assert.StartsWith($"Created the application {name}: kind Command, files {upload.Files.Name}", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveOnAnOlderRevisionGetsTheCurrentApplication()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView created = await administrator.CreatedApplicationAsync(Command(Unique("Editor")));

        HttpResponseMessage saved = await administrator.SaveApplicationAsync(created.Id, Keep(created) with { Publisher = "Contoso" });
        ApplicationView current = await RegisteredMachine.ReadAsync<ApplicationView>(saved);
        HttpResponseMessage stale = await administrator.SaveApplicationAsync(created.Id, Keep(created) with { Version = "2.0" });
        HttpResponseMessage unchanged = await administrator.SaveApplicationAsync(created.Id, Keep(current));

        Assert.Equal(2, current.Revision);
        Assert.Equal("Contoso", current.Publisher);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        ApplicationView newer = (await stale.Content.ReadFromJsonAsync<ApplicationView>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal((2L, "Contoso", (string?)null), (newer.Revision, newer.Publisher, newer.Version));
        Assert.Equal(2, (await RegisteredMachine.ReadAsync<ApplicationView>(unchanged)).Revision);
        Assert.Equal(
            [AuditActions.ApplicationCreated, AuditActions.ApplicationChanged],
            (await AuditAsync(created.Id)).Select(e => e.Action));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await administrator.SaveApplicationAsync(Guid.NewGuid(), Keep(created))).StatusCode);
    }

    [Fact]
    public async Task ANameIsTakenByABundleAndTheOtherWayRound()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string tools = Unique("Office tools");
        string suite = Unique("Suite");
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(tools));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(suite, app.Id));

        HttpResponseMessage bundleTakesApp = await administrator.PostAsync(BundlesPath, Bundle($" {tools.ToUpperInvariant().Replace(" ", "  ", StringComparison.Ordinal)} ", app.Id));
        HttpResponseMessage appTakesBundle = await administrator.PostAsync(ApplicationsPath, Command(suite.ToLowerInvariant()));
        HttpResponseMessage renamed = await administrator.SaveApplicationAsync(app.Id, Keep(app) with { Name = suite.ToUpperInvariant() });
        HttpResponseMessage bundleRenamed = await administrator.PutAsync($"{BundlesPath}/{bundle.Id:D}", Bundle(tools, app.Id) with { Revision = bundle.Revision });

        foreach (HttpResponseMessage refused in new[] { bundleTakesApp, appTakesBundle, renamed, bundleRenamed })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(["application.nameTaken"], await ErrorCodesAsync(refused, "name"));
        }
    }

    [Fact]
    public async Task FilesAndDependenciesMustBeInTheLibrary()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        PackageSummary filesPackage = await administrator.UploadedPackageAsync(TestZip.Create("setup.cmd"), UploadKind.Files);

        HttpResponseMessage unknownDependency = await administrator.PostAsync(ApplicationsPath, Command(Unique("Lonely"), null, Guid.NewGuid()));
        HttpResponseMessage unknownFiles = await administrator.PostAsync(ApplicationsPath, Command(Unique("Fileless"), Guid.NewGuid()));
        HttpResponseMessage notApplicationFiles = await administrator.PostAsync(ApplicationsPath, Command(Unique("Borrowed"), filesPackage.Id));

        Assert.Equal(["application.gone"], await ErrorCodesAsync(unknownDependency, "dependsOn[0]"));
        Assert.Equal(["application.filesGone"], await ErrorCodesAsync(unknownFiles, "packageId"));
        Assert.Equal(["application.filesGone"], await ErrorCodesAsync(notApplicationFiles, "packageId"));
    }

    [Fact]
    public async Task ACircleOfThreeIsRefusedWithItsNames()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView first = await administrator.CreatedApplicationAsync(Command(Unique("First")));
        ApplicationView second = await administrator.CreatedApplicationAsync(Command(Unique("Second"), null, first.Id));
        ApplicationView third = await administrator.CreatedApplicationAsync(Command(Unique("Third"), null, second.Id));

        HttpResponseMessage circle = await administrator.SaveApplicationAsync(first.Id, Keep(first) with { DependsOn = [third.Id] });
        HttpResponseMessage itself = await administrator.SaveApplicationAsync(first.Id, Keep(first) with { DependsOn = [first.Id] });

        Assert.Equal(["application.dependencyCycle"], await ErrorCodesAsync(circle, "dependsOn"));
        Assert.Equal(
            $"These applications depend on each other in a circle: {first.Name} → {third.Name} → {second.Name} → {first.Name}.",
            Assert.Single((await ProblemsAsync(circle))["dependsOn"]));
        Assert.Equal(["application.dependsOnItself"], await ErrorCodesAsync(itself, "dependsOn[0]"));
        Assert.Empty((await administrator.ApplicationAsync(first.Id)).DependsOn);
    }

    [Fact]
    public async Task ADependencyListsItsDependantsAndBundles()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView runtime = await administrator.CreatedApplicationAsync(Command(Unique("Runtime")));
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("App"), null, runtime.Id));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Base"), runtime.Id));

        Assert.Equal(
            [new ApplicationUse(bundle.Id, bundle.Name, ApplicationUseKind.Bundle), new ApplicationUse(app.Id, app.Name, ApplicationUseKind.Application)],
            (await administrator.ApplicationAsync(runtime.Id)).UsedBy);
        Assert.Empty((await administrator.ApplicationAsync(app.Id)).UsedBy);
    }

    [Fact]
    public async Task AWingetApplicationNeedsNoFiles()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SaveApplicationRequest firefox = Command(Unique("Firefox")) with
        {
            Kind = ApplicationKind.Winget,
            Command = null,
            WingetId = " Mozilla.Firefox ",
            SuccessExitCodes = [0, 0],
        };

        ApplicationView created = await administrator.CreatedApplicationAsync(firefox);

        Assert.Equal("Mozilla.Firefox", created.WingetId);
        Assert.Null(created.Files);
        Assert.Null(created.Command);
        Assert.Equal([0], created.SuccessExitCodes);
    }
}
