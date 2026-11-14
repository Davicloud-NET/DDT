// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.E2E;

// Windows packages added to the applied image in Windows PE of a dry run: a listed language and an update matched to
// the image's build.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class ServicingDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    // The test image is build 26100.
    private const int Build = 26100;

    private const string LanguagePack = "Microsoft-Windows-Client-Language-Pack_x64_ja-jp.cab";
    private const string Basic = "Microsoft-Windows-LanguageFeatures-Basic-ja-jp-Package~31bf3856ad364e35~amd64~~.cab";
    private const string Update = "windows11.0-kb5132061-x64_aedc878e9e078df2ec7e2cfb98dd5b21831cdbcc.msu";
    private const string Checkpoint = "windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task AddsTheListedLanguageAndTheMatchedUpdateInWindowsPE()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        PackageSummary language = await UploadAsync("e2e-ja-JP.zip", [LanguagePack, Basic], cancellationToken);
        PackageSummary update = await UploadAsync("e2e-update.zip", [Update, Checkpoint], cancellationToken);
        await MarkAsync(update, cancellationToken);
        ServicePackagesStep service = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Service packages",
            Packages = [language.Id],
            MatchingUpdates = true,
        };
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Servicing",
            [DryRunTests.Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, service],
            cancellationToken);
        Assert.Equal(8, sequence.Definition.Version);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        DeploymentStepView serviced = Assert.Single(run.Steps, step => step.StepId == service.Id);
        Assert.Equal((StepState.Done, SequencePhase.WindowsPE, (string?)null), (serviced.State, serviced.Phase, serviced.Error));

        Assert.Equal(1, agent.Output.Count($"Dry run: {LanguagePack} not added, and taken as added."));
        Assert.Equal(1, agent.Output.Count($"Dry run: {Basic} not added, and taken as added."));
        Assert.Equal(1, agent.Output.Count($"Dry run: {Update} not added, and taken as added."));
        Assert.Equal(0, agent.Output.Count($"Dry run: {Checkpoint}"));
        Assert.Equal(1, agent.Output.Count("Dry run: the image is not cleaned up."));
        Assert.Equal(1, agent.Output.Count($"Added {language.Name}; {language.Name}; {update.Name}."));

        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    private async Task<PackageSummary> UploadAsync(string name, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ddt-e2e-{Guid.NewGuid():N}-{name}");

        try
        {
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                {
                    using Stream entry = zip.CreateEntry(file).Open();
                    entry.Write(Guid.NewGuid().ToByteArray());
                }
            }

            return await lab.Api.UploadPackageAsync(path, UploadKind.WindowsPackage, cancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private Task<PackageSummary> MarkAsync(PackageSummary update, CancellationToken cancellationToken) => lab.Api.SendAsync(
        new JsonRequest<UpdatePackageRequest>(
            HttpMethod.Put,
            $"api/packages/{update.Id:D}",
            new UpdatePackageRequest(update.Name, null, [], Builds: [Build]),
            DdtJsonContext.Default.UpdatePackageRequest),
        DdtJsonContext.Default.PackageSummary,
        HttpStatusCode.OK,
        cancellationToken);
}
