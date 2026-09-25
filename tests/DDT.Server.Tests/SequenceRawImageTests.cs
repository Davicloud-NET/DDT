// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using System.Security.Cryptography;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// What the server checks in a sequence that writes a raw disk image, beyond the rules every agent checks too.
public sealed class SequenceRawImageTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private async Task<SequenceValidation> ValidateAsync(SequenceDefinition definition)
    {
        SignedInClient administrator = await application.AdministratorAsync();

        return await ReadAsync<SequenceValidation>(
            await administrator.PostAsync($"{SequenceRequests.Sequences}/validate", definition));
    }

    [Fact]
    public async Task StoresALinuxSequenceAsVersionTwoAndDescribesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        SequenceView created = await administrator.CreatedSequenceAsync(SequenceRequests.Linux(image.Id) with { Version = 1 });

        Assert.Equal(2, created.Definition.Version);
        Assert.Empty(created.Problems);
        Assert.Empty(created.Warnings);
        Assert.Equal([SequencePhase.WindowsPE, SequencePhase.WindowsPE], created.StepPhases);

        SequenceSummary summary = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == created.Id);
        Assert.True(summary.ErasesDisk);
        Assert.True(summary.NeedsComputerName);
        Assert.False(summary.ContinuesInWindows);
        Assert.Equal(image.Name, summary.RawImageName);
        Assert.Equal(ImageBootCapability.SecureBootOk, summary.RawImageBootCapability);
    }

    [Fact]
    public async Task NeedsAComputerNameOnlyWhenTheSeedUsesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceDefinition linux = SequenceRequests.Linux(image.Id);
        WriteCloudInitSeedStep seed = (WriteCloudInitSeedStep)linux.Steps[1];

        SequenceView unnamed = await administrator.CreatedSequenceAsync(
            linux with { Steps = [linux.Steps[0], seed with { MetaData = "instance-id: \"{{SmbiosUuid}}\"\n" }] });

        SequenceSummary summary = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == unnamed.Id);
        Assert.False(summary.NeedsComputerName);
    }

    [Fact]
    public async Task RefusesAnImageOfTheOtherKindInEachStep()
    {
        Image raw = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"disk {Guid.NewGuid():N}");
        Image wim = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"wim {Guid.NewGuid():N}");
        SequenceDefinition windows = SequenceRequests.Minimal(raw.Id);
        SequenceDefinition linux = SequenceRequests.Linux(wim.Id);

        SequenceProblem applied = Assert.Single((await ValidateAsync(windows)).Problems);
        SequenceProblem written = Assert.Single((await ValidateAsync(linux)).Problems);

        Assert.Equal(
            new SequenceProblem(windows.Steps[1].Id, "imageId", $"{raw.Name} is a raw disk image, which a Write raw disk image step writes. Choose a Windows image."),
            applied);
        Assert.Equal(
            new SequenceProblem(linux.Steps[0].Id, "imageId", $"{wim.Name} is a Windows image, which an Apply image step applies. Choose a raw disk image."),
            written);
        Assert.Equal(
            "Choose the raw disk image to write.",
            Assert.Single((await ValidateAsync(SequenceRequests.Linux(Guid.Empty))).Problems).Message);
    }

    [Fact]
    public async Task WarnsOfAnImageThatDoesNotStartWithSecureBootOn()
    {
        Image unsigned = await application.SeedRawImageAsync(
            RandomNumberGenerator.GetBytes(4096),
            ImageBootCapability.NotSigned,
            name: $"custom {Guid.NewGuid():N}");
        Image arm = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), architecture: "arm64", name: $"arm {Guid.NewGuid():N}");
        SequenceDefinition definition = SequenceRequests.Linux(unsigned.Id);

        SequenceValidation validation = await ValidateAsync(definition);

        Assert.Empty(validation.Problems);
        Assert.Equal(
            new SequenceProblem(
                definition.Steps[0].Id,
                "imageId",
                $@"{unsigned.Name} will not start with Secure Boot on. \EFI\BOOT\BOOTX64.EFI carries no signature. Turn Secure Boot off in the " +
                "firmware of the machines it goes to, or enroll your own key. Assigning the sequence then asks to allow it."),
            Assert.Single(validation.Warnings));
        Assert.Equal(
            $"{arm.Name} starts arm64 machines, and DDT writes images for x64 machines. Choose an x64 image.",
            Assert.Single((await ValidateAsync(SequenceRequests.Linux(arm.Id))).Problems).Message);
    }

    [Fact]
    public async Task WarnsOfPlaceholdersDdtDoesNotFillIn()
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceDefinition linux = SequenceRequests.Linux(image.Id);
        WriteCloudInitSeedStep seed = (WriteCloudInitSeedStep)linux.Steps[1] with
        {
            UserData = "## template: jinja\n#cloud-config\nfqdn: {{ v1.local_hostname }}.example\nhostname: \"{{Hostname}}\"\n",
            NetworkConfig = "{{Gateway}} {{Dns}}",
        };

        SequenceValidation validation = await ValidateAsync(linux with { Steps = [linux.Steps[0], seed] });

        Assert.Empty(validation.Problems);
        Assert.Equal(["userData", "networkConfig"], validation.Warnings.Select(warning => warning.Field));
        Assert.Equal(
            "{{Hostname}} is not one of DDT's placeholders, so it stays as it is. DDT fills in {{ComputerName}}, {{Manufacturer}}, " +
            "{{Model}}, {{SerialNumber}}, {{SmbiosUuid}}, {{MacAddress}}.",
            validation.Warnings[0].Message);
        Assert.StartsWith("{{Gateway}}, {{Dns}} are not DDT's placeholders", validation.Warnings[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OffersTheInstallLinuxTemplate()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        SequenceTemplate linux = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceTemplate>>(await administrator.GetAsync($"{SequenceRequests.Sequences}/templates")),
            template => template.Key == SequenceTemplates.InstallLinuxKey);

        Assert.Equal(2, linux.Definition.Version);
        Assert.Equal(Guid.Empty, Assert.IsType<WriteRawImageStep>(linux.Definition.Steps[0]).ImageId);
        WriteCloudInitSeedStep seed = Assert.IsType<WriteCloudInitSeedStep>(linux.Definition.Steps[1]);
        Assert.Equal(SequenceTemplates.LinuxMetaData, seed.MetaData);
        Assert.Null(seed.NetworkConfig);

        // Only the image is missing.
        Assert.Equal("imageId", Assert.Single((await ValidateAsync(linux.Definition)).Problems).Field);
    }
}
