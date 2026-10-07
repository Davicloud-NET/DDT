// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Core.Boot;

namespace DDT.Agent.Sequences;

// Whether a raw disk image may be written here. Firmware with Secure Boot on won't start an unsigned image, or one
// signed under a Microsoft UEFI CA it doesn't trust. The machine would start nothing, unless the run was allowed the
// mismatch.
public static class SecureBootGate
{
    // Why the image must not be written, or null. trustedUefiCas says which of Microsoft's third-party UEFI CAs the
    // firmware trusts, null when that is not known.
    public static string? Refusal(AgentRunImage image, bool allowed, bool? secureBootEnabled, UefiCa? trustedUefiCas)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (allowed || secureBootEnabled != true || Why(image, trustedUefiCas) is not { } why)
        {
            return null;
        }

        string remedy = Untrusted(image, trustedUefiCas)
            ? "Allow that CA or turn Secure Boot off in the firmware setup"
            : "Turn Secure Boot off in the firmware setup";

        return $"{image.Name} {why}, and this machine has Secure Boot on, so it would not start. {remedy}, or start the run again " +
            "allowing the image.";
    }

    // What the log says when the image is written although it may not start, or null.
    public static string? Warning(AgentRunImage image, bool allowed, bool? secureBootEnabled, UefiCa? trustedUefiCas)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (Why(image, trustedUefiCas) is not { } why)
        {
            return null;
        }

        string starts = Untrusted(image, trustedUefiCas)
            ? "once that CA is allowed or Secure Boot is turned off in the firmware setup"
            : "once Secure Boot is turned off in the firmware setup, or your own key is enrolled";

        return secureBootEnabled switch
        {
            true when allowed =>
                $"{image.Name} {why}, and this machine has Secure Boot on. The run was allowed to write it: the machine starts it {starts}.",
            null => $"{image.Name} {why}, and Windows PE does not say whether Secure Boot is on. If it is, the machine " +
                $"{(image.BootCapability == ImageBootCapability.Unknown ? "may not" : "will not")} start the image.",
            _ => null,
        };
    }

    // What the log says when the image is signed for Secure Boot but the firmware's db doesn't say which CAs it trusts.
    // DDT then can't tell whether the machine will start it. Null otherwise.
    public static string? Unknown(AgentRunImage image, bool? secureBootEnabled, UefiCa? trustedUefiCas)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.BootCapability == ImageBootCapability.SecureBootOk && secureBootEnabled == true && trustedUefiCas is null
            ? $"The firmware's list of trusted certificates could not be read, so DDT cannot tell whether this machine trusts " +
                $"{MicrosoftUefiCa.Describe(image.SignedUnder)}, which {image.Name} is signed under."
            : null;
    }

    private static bool Untrusted(AgentRunImage image, UefiCa? trustedUefiCas) =>
        image.BootCapability == ImageBootCapability.SecureBootOk && MicrosoftUefiCa.Untrusted(trustedUefiCas, image.SignedUnder);

    // Why the machine would not start the image with Secure Boot on, or null when it would.
    private static string? Why(AgentRunImage image, UefiCa? trustedUefiCas) => image.BootCapability switch
    {
        ImageBootCapability.NotSigned => "is not signed for Secure Boot",
        ImageBootCapability.SecureBootOk when MicrosoftUefiCa.Untrusted(trustedUefiCas, image.SignedUnder) =>
            $"is signed under {MicrosoftUefiCa.Describe(image.SignedUnder)}, which this machine's firmware does not trust",
        ImageBootCapability.SecureBootOk => null,
        _ => "may not start with Secure Boot on, as DDT could not tell whether it is signed for it",
    };
}
