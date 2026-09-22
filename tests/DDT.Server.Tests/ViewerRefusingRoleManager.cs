// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// The host's role manager, except that the Viewer role, the last one the bootstrap creates, cannot be created.
public sealed class ViewerRefusingRoleManager(IServiceProvider services) : RoleManager<DdtRole>(
    services.GetRequiredService<IRoleStore<DdtRole>>(),
    services.GetServices<IRoleValidator<DdtRole>>(),
    services.GetRequiredService<ILookupNormalizer>(),
    services.GetRequiredService<IdentityErrorDescriber>(),
    services.GetRequiredService<ILogger<RoleManager<DdtRole>>>())
{
    public override Task<IdentityResult> CreateAsync(DdtRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return role.Name == DdtRoleNames.Viewer
            ? Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Refused", Description = "The test refuses the Viewer role." }))
            : base.CreateAsync(role);
    }
}
