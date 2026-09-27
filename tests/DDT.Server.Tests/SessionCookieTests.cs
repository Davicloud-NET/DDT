// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Security;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace DDT.Server.Tests;

// The session cookie is Lax whether single sign-on is on or not, so that the provider's navigation back to DDT keeps
// the session it starts.
public sealed class SessionCookieTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSessionCookieIsLax(bool singleSignOn)
    {
        using SettingsApplication application = singleSignOn
            ? new(("DDT:Oidc:Enabled", "true"), ("DDT:Oidc:Authority", "https://idp.example"), ("DDT:Oidc:ClientId", "ddt"))
            : new();
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        using HttpClient http = application.CreateDefaultClient();

        using HttpResponseMessage session = await http.GetAsync(new Uri("/api/auth/session", UriKind.Relative), TestContext.Current.CancellationToken);
        SetCookieHeaderValue antiforgery = SetCookieHeaderValue.ParseList([.. session.Headers.GetValues(HeaderNames.SetCookie)]).Single();

        using HttpRequestMessage login = new(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginRequest(userName, DdtApplication.Password, null, null), options: TestJson.Options),
        };

        login.Headers.Add(HeaderNames.Cookie, $"{antiforgery.Name}={antiforgery.Value}");
        login.Headers.Add(CsrfHeaderNames.RequestToken, session.Headers.GetValues(CsrfHeaderNames.RequestToken).Single());

        using HttpResponseMessage response = await http.SendAsync(login, TestContext.Current.CancellationToken);

        SetCookieHeaderValue cookie = Assert.Single(
            SetCookieHeaderValue.ParseList([.. response.Headers.GetValues(HeaderNames.SetCookie)]),
            candidate => candidate.Name == "ddt-auth");
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.True(cookie.HttpOnly);
    }
}
