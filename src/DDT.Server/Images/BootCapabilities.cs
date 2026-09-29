// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using DDT.Core.Disks;

namespace DDT.Server.Images;

// Judges a raw disk image by \EFI\BOOT\BOOTX64.EFI. Firmware starts this file from the EFI system partition, and DDT's
// boot entry starts it too. The shims next to a distribution's boot loader only help explain an unsigned fallback.
public static class BootCapabilities
{
    // Fills in a sentence such as "noble will not start with Secure Boot on". An image whose boot file isn't signed for
    // Secure Boot will not start. An image DDT couldn't judge may not start.
    public static string NotStarting(ImageBootCapability? capability) =>
        capability == ImageBootCapability.Unknown ? "may not start" : "will not start";

    // The same choice as a message argument, which picks the message's wording. It's "maybe" for an image DDT couldn't
    // judge, and "never" otherwise.
    public static string NotStartingChoice(ImageBootCapability? capability) =>
        capability == ImageBootCapability.Unknown ? "maybe" : "never";

    public const string FallbackPath = @"\EFI\BOOT\BOOTX64.EFI";

    private static readonly Dictionary<string, string> s_otherFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"\EFI\BOOT\BOOTAA64.EFI"] = "arm64",
        [@"\EFI\BOOT\BOOTIA32.EFI"] = "x86",
    };

    public static BootAssessment Assess(RawImageInfo info, IReadOnlyCollection<X509Certificate2> trusted)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(trusted);

        RawImageBootFile? fallback = info.BootFiles.FirstOrDefault(file => string.Equals(file.Path, FallbackPath, StringComparison.OrdinalIgnoreCase));

        if (fallback is null)
        {
            // If BOOTX64.EFI exists but can't be read, for example because it's too large, don't guess the processor
            // from the other boot files.
            bool fallbackUnreadable = info.UnreadableBootFiles?.Any(path => string.Equals(path, FallbackPath, StringComparison.OrdinalIgnoreCase)) == true;

            if (!fallbackUnreadable && info.BootFiles.FirstOrDefault(file => s_otherFallbacks.ContainsKey(file.Path)) is { } other)
            {
                return new BootAssessment(
                    ImageBootCapability.Unknown,
                    $"The image is for {s_otherFallbacks[other.Path]} machines: it has {other.Path} and no {FallbackPath}.",
                    s_otherFallbacks[other.Path]);
            }

            return new BootAssessment(
                ImageBootCapability.Unknown,
                info.BootProblem is { } problem
                    ? $"{problem} DDT cannot tell whether it starts with Secure Boot on."
                    : $"The image has no {FallbackPath}, the file DDT's boot entry starts.",
                null);
        }

        AuthenticodeResult result = Authenticode.Check(fallback.Content, trusted);
        string? architecture = result.Machine switch
        {
            PeImage.MachineAmd64 => "x64",
            PeImage.MachineArm64 => "arm64",
            PeImage.MachineI386 => "x86",
            _ => null,
        };

        UefiCa signedUnder = (result.Anchors ?? []).Aggregate(UefiCa.None, (cas, anchor) => cas | MicrosoftUefiCa.Of(anchor.RawData));

        return result.Status switch
        {
            AuthenticodeStatus.Trusted => new BootAssessment(
                ImageBootCapability.SecureBootOk,
                $"{FallbackPath} is signed by {result.Signer} under {MicrosoftUefiCa.Describe(signedUnder)}, which PCs trust unless their " +
                "firmware lacks it or turns it off, as Secured-core PCs do.",
                architecture,
                signedUnder),
            AuthenticodeStatus.SignedByOthers => new BootAssessment(
                ImageBootCapability.NotSigned,
                $"{FallbackPath} is signed by {result.Signer}, which Microsoft's UEFI CA did not certify.{ShimNote(info, trusted)}",
                architecture),
            AuthenticodeStatus.NotSigned => new BootAssessment(
                ImageBootCapability.NotSigned,
                $"{FallbackPath} {result.Reason}.{ShimNote(info, trusted)}",
                architecture),
            _ => new BootAssessment(ImageBootCapability.Unknown, $"{FallbackPath} {result.Reason}.", architecture),
        };
    }

    private static string ShimNote(RawImageInfo info, IReadOnlyCollection<X509Certificate2> trusted)
    {
        RawImageBootFile? shim = info.BootFiles
            .Where(file => !string.Equals(file.Path, FallbackPath, StringComparison.OrdinalIgnoreCase) && !s_otherFallbacks.ContainsKey(file.Path))
            .FirstOrDefault(file => Authenticode.Check(file.Content, trusted).Status == AuthenticodeStatus.Trusted);

        return shim is null ? "" : $" {shim.Path} is signed under Microsoft's UEFI CA, but DDT's boot entry starts {FallbackPath}.";
    }
}
