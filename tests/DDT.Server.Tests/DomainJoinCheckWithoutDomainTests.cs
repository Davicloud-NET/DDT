// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DomainJoinCheckWithoutDomainTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task WithoutADomainTheCheckSaysWhatToSet()
    {
        HttpResponseMessage response = await (await application.AdministratorAsync()).PostAsync("/api/deployments/domain-check", new DomainJoinCheckRequest(null));
        DomainJoinCheckView result = await RegisteredMachine.ReadAsync<DomainJoinCheckView>(response);

        Assert.False(result.CanJoin);
        Assert.Equal("No domain is set. Set the domain and the join account on the Deployment defaults page.", Assert.Single(result.Findings).Text);
    }
}
