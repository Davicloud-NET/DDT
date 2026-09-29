// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Endpoints;

// The services the handlers for signing in and for an account's own credentials need, bound with [AsParameters].
internal sealed record SignInServices(
    SignInManager<DdtUser> SignInManager,
    UserManager<DdtUser> UserManager,
    UserActivity Activity,
    ILoggerFactory LoggerFactory);
