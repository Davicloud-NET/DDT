// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Core.Disks;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class BootCapabilitiesTests
{
    private static readonly GptLayout s_table = GptLayout.Create(16384, Guid.Parse("0193a4b2-0000-4000-8000-0000000000c1"));

    // Another processor's boot file next to a BOOTX64.EFI that cannot be read does not make the image that processor's.
    [Fact]
    public void TakesAnImageWithAnUnreadableX64BootFileForNoOtherProcessor()
    {
        RawImageInfo info = new(
            8L * 1024 * 1024,
            s_table,
            null,
            [new RawImageBootFile(@"\EFI\BOOT\BOOTAA64.EFI", TestPe.Create(PeImage.MachineArm64))],
            @"\EFI\BOOT\BOOTX64.EFI cannot be read: it is larger than 33554432 bytes.",
            [@"\EFI\BOOT\BOOTX64.EFI"]);

        BootAssessment assessment = BootCapabilities.Assess(info, UefiCertificateAuthorities.Microsoft);

        Assert.Equal((ImageBootCapability.Unknown, (string?)null), (assessment.Capability, assessment.Architecture));
        Assert.StartsWith(@"\EFI\BOOT\BOOTX64.EFI cannot be read", assessment.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TakesAnImageWithOnlyAnotherProcessorsBootFileForThatProcessor()
    {
        RawImageInfo info = new(
            8L * 1024 * 1024,
            s_table,
            null,
            [new RawImageBootFile(@"\EFI\BOOT\BOOTAA64.EFI", TestPe.Create(PeImage.MachineArm64))],
            null);

        Assert.Equal("arm64", BootCapabilities.Assess(info, UefiCertificateAuthorities.Microsoft).Architecture);
    }
}
