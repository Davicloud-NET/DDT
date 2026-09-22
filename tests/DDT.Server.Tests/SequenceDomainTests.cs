// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SequenceDomainTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    // The join runs in Windows, where it needs the local administrator to get past setup, and a restart to take effect.
    [Fact]
    public async Task TheTemplateJoinsTheConfiguredDomainInWindows()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        SequenceTemplate template = Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceTemplate>>(
            await administrator.GetAsync($"{SequenceRequests.Sequences}/templates?imageId={image.Id}")));

        Assert.Equal(5, template.Definition.Steps.Count);
        Assert.True(Assert.IsType<WriteUnattendStep>(template.Definition.Steps[3]).LocalAdministrator);
        JoinDomainStep join = Assert.IsType<JoinDomainStep>(template.Definition.Steps[4]);
        Assert.Equal(
            SequenceRequests.Json(new JoinDomainStep { Id = join.Id, Name = "Join the domain", RebootAfter = true }),
            SequenceRequests.Json(join));

        SequenceView created = await administrator.CreatedSequenceAsync(template.Definition);
        Assert.Empty(created.Problems);
        Assert.Empty(created.Warnings);
        Assert.Equal(SequencePhase.Windows, created.StepPhases[^1]);

        SequenceSummary summary = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == created.Id);
        Assert.True(summary.NeedsComputerName);
        Assert.True(summary.ContinuesInWindows);
    }
}
