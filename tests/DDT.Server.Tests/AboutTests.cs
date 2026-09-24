// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AboutTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Legal = "/api/about/legal/";

    [Fact]
    public async Task AnyoneLearnsWhatRunsAndUnderWhichLicence()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient anonymous = Anonymous();

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri("/api/about", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument about = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement root = about.RootElement;
        Assert.Equal("DDT", root.GetProperty("product").GetString());
        Assert.Equal(
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            root.GetProperty("version").GetString());
        Assert.Equal("DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.", root.GetProperty("attribution").GetString());
        Assert.Equal("GPL-3.0-or-later", root.GetProperty("license").GetString());
        Assert.Equal("https://github.com/Davicloud-NET/DDT", root.GetProperty("sourceUrl").GetString());
        Assert.Equal(LegalFilesInRepository(), root.GetProperty("legalDocuments").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public async Task AnyoneReadsEveryListedDocumentAsTheRepositoryHoldsIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient anonymous = Anonymous();
        string root = Repository.Root();

        foreach (string name in LegalFilesInRepository())
        {
            using HttpResponseMessage response = await anonymous.GetAsync(new Uri(Legal + name, UriKind.Relative), cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(root, name), cancellationToken),
                await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
    }

    // The CSRF filters of the /api group only look at requests that change something, so a link from anywhere works.
    [Fact]
    public async Task ALinkFromAnotherSiteOpensTheLicence()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient anonymous = Anonymous();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(Legal + "LICENSE", UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Origin", "https://elsewhere.example");

        using HttpResponseMessage response = await anonymous.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("COPYING")]
    [InlineData("license")]
    [InlineData("licenses")]
    [InlineData("licenses/")]
    [InlineData("licenses%2Fwimlib%2FCOPYING")]
    [InlineData("..%2FDDT.Host.dll")]
    [InlineData("..%5Cappsettings.json")]
    [InlineData("licenses/..%2F..%2Fappsettings.json")]
    public async Task OnlyAListedNameIsServed(string name)
    {
        using HttpClient anonymous = Anonymous();

        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(Legal + name, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // An HTTP client removes the dot segments before it sends a request, so the path is set on the request directly.
    [Theory]
    [InlineData("licenses/../../appsettings.json")]
    [InlineData("licenses/../LICENSE")]
    [InlineData("./NOTICE")]
    [InlineData("licenses\\..\\..\\appsettings.json")]
    public async Task APathWithDotSegmentsIsNotFollowed(string name)
    {
        HttpContext context = await application.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = Legal + name;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task TheFullPathOfADocumentIsNotAName()
    {
        using HttpClient anonymous = Anonymous();
        string fullPath = Path.Combine(AppContext.BaseDirectory, "legal", "LICENSE");
        Assert.True(File.Exists(fullPath));

        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(Legal + Uri.EscapeDataString(fullPath), UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A request that misses the About endpoints is redirected to the sign-in page, and following that would end on the
    // web UI with 200.
    private HttpClient Anonymous() => application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // The build copies LICENSE, NOTICE, THIRD-PARTY-NOTICES.md and licenses/ into the legal folder under the same names.
    private static List<string> LegalFilesInRepository()
    {
        string root = Repository.Root();
        IEnumerable<string> licenses = Directory.EnumerateFiles(Path.Combine(root, "licenses"), "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

        return ["LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md", .. licenses];
    }
}
