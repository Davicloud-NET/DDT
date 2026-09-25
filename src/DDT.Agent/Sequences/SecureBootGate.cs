// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;

namespace DDT.Agent.Sequences;

// Whether a raw disk image may be written on this machine. Firmware with Secure Boot on refuses to start an image that
// is not signed for it, so writing one there leaves a machine that starts nothing, unless whoever started the run
// allowed it.
public static class SecureBootGate
{
    // Why the image must not be written, or null.
    public static string? Refusal(AgentRunImage image, bool allowed, bool? secureBootEnabled)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.BootCapability == ImageBootCapability.SecureBootOk || allowed || secureBootEnabled != true
            ? null
            : $"{image.Name} {Why(image)}, and this machine has Secure Boot on, so it would not start. Turn Secure Boot off in the " +
                "firmware setup, or start the run again allowing the image.";
    }

    // What the log says when the image is written although it may not start, or null.
    public static string? Warning(AgentRunImage image, bool allowed, bool? secureBootEnabled)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.BootCapability == ImageBootCapability.SecureBootOk)
        {
            return null;
        }

        return secureBootEnabled switch
        {
            true when allowed =>
                $"{image.Name} {Why(image)}, and this machine has Secure Boot on. The run was allowed to write it: the machine starts it " +
                "once Secure Boot is turned off in the firmware setup, or your own key is enrolled.",
            null => $"{image.Name} {Why(image)}, and Windows PE does not say whether Secure Boot is on. If it is, the machine " +
                $"{(image.BootCapability == ImageBootCapability.NotSigned ? "will not" : "may not")} start the image.",
            _ => null,
        };
    }

    private static string Why(AgentRunImage image) => image.BootCapability == ImageBootCapability.NotSigned
        ? "is not signed for Secure Boot"
        : "may not start with Secure Boot on, as DDT could not tell whether it is signed for it";
}
