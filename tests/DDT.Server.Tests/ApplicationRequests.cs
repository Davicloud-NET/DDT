// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Applications;
using DDT.Contracts.Images;
using Xunit;

namespace DDT.Server.Tests;

// The application library as the SPA uses it.
internal static class ApplicationRequests
{
    public const string ApplicationsPath = "/api/applications";
    public const string BundlesPath = "/api/application-bundles";

    // Names are unique across the test class's database.
    public static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    public static SaveApplicationRequest Command(string name, Guid? packageId = null, params Guid[] dependsOn) =>
        new(0, name, null, null, null, ApplicationKind.Command, packageId, "setup.cmd /quiet", [0], [3010], null, dependsOn, null, null, 60, false);

    // What the page sends back for an application it shows unchanged.
    public static SaveApplicationRequest Keep(ApplicationView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new SaveApplicationRequest(
            view.Revision,
            view.Name,
            view.Description,
            view.Publisher,
            view.Version,
            view.Kind,
            view.Files?.PackageId,
            view.Command,
            view.SuccessExitCodes,
            view.RestartExitCodes,
            view.Detection,
            view.DependsOn,
            view.WingetId,
            view.WingetVersion,
            view.TimeoutMinutes,
            view.ContinueOnFailure);
    }

    public static SaveApplicationBundleRequest Bundle(string name, params Guid[] members) => new(0, name, null, members);

    public static async Task<ApplicationUpload> UploadedApplicationAsync(this SignedInClient client, byte[] file, string fileName)
    {
        HttpResponseMessage response = await client.UploadPackageAsync(file, UploadKind.Application, fileName);

        return await RegisteredMachine.ReadAsync<ApplicationUpload>(response);
    }

    public static async Task<ApplicationView> CreatedApplicationAsync(this SignedInClient client, SaveApplicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await RegisteredMachine.ReadAsync<ApplicationView>(await client.PostAsync(ApplicationsPath, request));
    }

    public static Task<HttpResponseMessage> SaveApplicationAsync(this SignedInClient client, Guid id, SaveApplicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PutAsync($"{ApplicationsPath}/{id:D}", request);
    }

    public static async Task<ApplicationView> ApplicationAsync(this SignedInClient client, Guid id)
    {
        ArgumentNullException.ThrowIfNull(client);

        IReadOnlyList<ApplicationView> applications = await RegisteredMachine.ReadAsync<IReadOnlyList<ApplicationView>>(await client.GetAsync(ApplicationsPath));

        return Assert.Single(applications, application => application.Id == id);
    }

    public static async Task<ApplicationBundleView> CreatedBundleAsync(this SignedInClient client, SaveApplicationBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await RegisteredMachine.ReadAsync<ApplicationBundleView>(await client.PostAsync(BundlesPath, request));
    }

    public static async Task<string[]> ErrorCodesAsync(HttpResponseMessage response, string field)
    {
        ArgumentNullException.ThrowIfNull(response);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return [.. problem.RootElement.GetProperty("errorCodes").GetProperty(field).EnumerateArray().Select(code => code.GetProperty("code").GetString() ?? "")];
    }

    public static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return problem.RootElement.GetProperty("code").GetString() ?? "";
    }
}
