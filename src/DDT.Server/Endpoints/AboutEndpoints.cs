// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.About;
using DDT.Server.About;
using DDT.Server.Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

public static class AboutEndpoints
{
    public static RouteGroupBuilder MapAboutEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Anonymous, because the licence has to be readable by everyone the web UI shows its sign-in page to.
        group.MapGet("/", GetAbout).AllowAnonymous();
        group.MapGet("/legal/{**path}", GetLegalDocument).AllowAnonymous();

        // Anonymous, so a browser can fetch the root to trust before anyone can sign in over a connection it trusts.
        group.MapGet("/root-certificate", GetRootCertificate).AllowAnonymous();

        return group;
    }

    private static Ok<AboutInfo> GetAbout(AboutCatalog catalog) => TypedResults.Ok(catalog.Info);

    private static Results<PhysicalFileHttpResult, NotFound> GetLegalDocument(string? path, AboutCatalog catalog) =>
        catalog.TryGetDocument(path, out string? file)
            ? TypedResults.PhysicalFile(file, "text/plain; charset=utf-8")
            : TypedResults.NotFound();

    // Not found while the served certificate is an administrator's rather than one from DDT's root.
    private static Results<FileContentHttpResult, NotFound> GetRootCertificate([FromServices] ServerCertificates? certificates) =>
        certificates?.RootCertificatePem is { } pem
            ? TypedResults.File(Encoding.ASCII.GetBytes(pem), "application/x-pem-file", "ddt-root.pem")
            : TypedResults.NotFound();
}
