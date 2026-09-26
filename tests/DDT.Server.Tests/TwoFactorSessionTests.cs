// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Authentication;
using Xunit;

namespace DDT.Server.Tests;

// The server's clock stands still here, so a test can move it past the interval at which a session's security stamp
// is checked again.
public sealed class TwoFactorSessionTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    // Setting up an authenticator makes a new key, which changes the security stamp. The session that set it up is
    // signed in again with the new stamp, so the person is not signed out while they scan the code.
    [Fact]
    public async Task SettingUpAnAuthenticatorKeepsTheSessionThatDidIt()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        using HttpResponseMessage enrolled = await viewer.PostAsync("/api/auth/2fa/enroll");
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);

        application.Clock.Advance(TimeSpan.FromMinutes(5));

        using HttpResponseMessage me = await viewer.Http.GetAsync(new Uri("/api/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
