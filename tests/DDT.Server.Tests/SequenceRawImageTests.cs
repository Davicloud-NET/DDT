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
    public async Task StoresALinuxSequenceAsVersionFourAndDescribesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        SequenceView created = await administrator.CreatedSequenceAsync(SequenceRequests.Linux(image.Id) with { Version = 1 });

        Assert.Equal(4, created.Definition.Version);
        Assert.Empty(created.Problems);
        Assert.Empty(created.Warnings);
        Assert.Equal([SequencePhase.LinuxBoot, SequencePhase.LinuxBoot], created.StepPhases);
        Assert.NotNull(created.NodePhases);
        Assert.All(created.NodePhases, node => Assert.Equal([SequencePhase.LinuxBoot], node.Phases));

        SequenceSummary summary = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == created.Id);
        Assert.True(summary.ErasesDisk);
        Assert.True(summary.NeedsComputerName);
        Assert.False(summary.ContinuesInWindows);
        Assert.Equal(SequenceKind.Linux, summary.Kind);
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
            "{{Model}}, {{SerialNumber}}, {{SmbiosUuid}}, {{MacAddress}}, {{AdministratorName}}, {{Keyboard}}, {{Locale}}, " +
            "{{OrganizationalUnit}}, {{TimeZone}}.",
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

        Assert.Equal(5, linux.Definition.Version);
        Assert.Equal(Guid.Empty, Assert.IsType<WriteRawImageStep>(linux.Definition.Steps[0]).ImageId);
        WriteCloudInitSeedStep seed = Assert.IsType<WriteCloudInitSeedStep>(linux.Definition.Steps[1]);
        Assert.Equal(SequenceTemplates.LinuxMetaData, seed.MetaData);
        Assert.Equal(SequenceTemplates.LinuxUserData, seed.UserData);
        Assert.Null(seed.NetworkConfig);

        // Only the image is missing, and the seed's user-data lets the run go on in the installed Linux.
        SequenceValidation validation = await ValidateAsync(linux.Definition);
        Assert.Equal("imageId", Assert.Single(validation.Problems).Field);
        Assert.Empty(validation.Warnings);
        Assert.Empty(validation.Notes ?? []);
    }

    // A sample to replace: one line for the run's log, from a script that restarts the machine with exit code 194.
    [Fact]
    public async Task TheInstallLinuxTemplateGoesOnWithASampleScriptInTheInstalledLinux()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        SequenceTemplate linux = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceTemplate>>(await administrator.GetAsync($"{SequenceRequests.Sequences}/templates")),
            template => template.Key == SequenceTemplates.InstallLinuxKey);
        RunScriptStep sample = Assert.IsType<RunScriptStep>(Assert.Single(linux.Definition.Steps.Skip(2)));

        Assert.Equal(
            (SequencePhase.Linux, ScriptInterpreter.Shell, "Say where the run goes on"),
            (sample.Phase, sample.Interpreter, sample.Name));
        Assert.Equal([0], sample.SuccessExitCodes);
        Assert.Equal([194], sample.RebootExitCodes);
        Assert.Null(sample.PackageId);
        Assert.Equal(
            "# Runs as root in the installed Linux, once cloud-init has finished. Replace it with what the machine needs.\n" +
            "echo \"The run goes on in $(uname -sr) on $(hostname), as $(id -un).\"\n",
            sample.Script);
        Assert.Equal(
            "Writes a raw disk image, such as a distribution's cloud image, and a cloud-init seed that names the machine, then runs a " +
            "script in the installed Linux.",
            linux.Description);
    }

    // cloud-init 25 and 26 end "degraded" on a key with an empty list, and on user-data that holds comments alone.
    [Fact]
    public void TheInstallLinuxTemplatesUserDataHoldsAKeyAndNoEmptyList()
    {
        string[] lines = SequenceTemplates.LinuxUserData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("#cloud-config", lines[0]);
        Assert.Equal(["users:", "  - default"], lines.Where(line => !line.StartsWith('#')));
        Assert.Equal(["# ssh_authorized_keys:", "#   - ssh-ed25519 AAAA... name@example.org"], lines[^2..]);
        Assert.DoesNotContain("[]", SequenceTemplates.LinuxUserData, StringComparison.Ordinal);
        Assert.EndsWith("\n", SequenceTemplates.LinuxUserData, StringComparison.Ordinal);
    }

    // A sequence saves with such a seed as a draft, and its row in the list is made without reading the text.
    [Fact]
    public async Task ASeedOverTheLimitDrawsTheLimitsProblemAndNothingOfItsUserData()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceDefinition definition = SequenceRequests.LinuxWithInstalledSteps(image.Id);
        WriteCloudInitSeedStep seed = Assert.IsType<WriteCloudInitSeedStep>(definition.Steps[1]) with
        {
            UserData = "#cloud-config\nvendor_data: {enabled: false}\n# " + new string('x', 64 * 1024),
        };

        SequenceView saved = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(definition.Steps[0], seed, definition.Steps[2]));
        SequenceSummary row = Assert.Single(
            await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            summary => summary.Id == saved.Id);

        Assert.Equal("sequence.seedFileTooLarge", Assert.Single(saved.Problems).Code);
        Assert.Empty(saved.Warnings);
        Assert.Equal((1, 0, true), (row.ProblemCount, row.WarningCount, row.ContinuesInLinux));
    }
}
