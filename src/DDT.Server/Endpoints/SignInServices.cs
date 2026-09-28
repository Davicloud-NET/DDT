// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Endpoints;

// What the handlers of an account's own sign-in and credentials use, bound with [AsParameters].
internal sealed record SignInServices(
    SignInManager<DdtUser> SignInManager,
    UserManager<DdtUser> UserManager,
    UserActivity Activity,
    ILoggerFactory LoggerFactory);
