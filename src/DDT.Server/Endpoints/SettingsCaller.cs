// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Endpoints;

// The administrator behind a settings request, bound with [AsParameters].
internal sealed record SettingsCaller(HttpContext Context, ReauthenticationTokens Reauthentication, UserManager<DdtUser> Users)
{
    public Actor Actor => SettingsEndpoints.SettingsActor(Context);

    // Whether the request carries a fresh proof of identity, which a change that grants roles or trust, or runs as
    // SYSTEM on machines, needs.
    public Task<bool> ReauthenticatedAsync() => Reauthentication.ValidAsync(Context, Context.User, Users);
}
