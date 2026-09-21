// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Tests;

// The host's user manager, except that every role assignment fails, which no valid configuration can make happen.
public sealed class RoleRefusingUserManager(IServiceProvider services) : UserManager<DdtUser>(
    services.GetRequiredService<IUserStore<DdtUser>>(),
    services.GetRequiredService<IOptions<IdentityOptions>>(),
    services.GetRequiredService<IPasswordHasher<DdtUser>>(),
    services.GetServices<IUserValidator<DdtUser>>(),
    services.GetServices<IPasswordValidator<DdtUser>>(),
    services.GetRequiredService<ILookupNormalizer>(),
    services.GetRequiredService<IdentityErrorDescriber>(),
    services,
    services.GetRequiredService<ILogger<UserManager<DdtUser>>>())
{
    public override Task<IdentityResult> AddToRoleAsync(DdtUser user, string role) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Refused", Description = "The test refuses every role." }));
}
