// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Boot;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// Whether a run may write a raw disk image the machine will not start with Secure Boot on. The agent checks the firmware
// again before it writes.
internal static class SecureBootPolicy
{
    // Written only where someone allowed it for the run, or where the machine did not say Secure Boot is on. Allow is kept
    // only where it matters; Problem is the refusal when Secure Boot is on and nobody allowed the image.
    public static (bool Allow, ServerMessage? Problem) Decide(
        Machine machine,
        SequenceDefinition definition,
        SequenceReferences references,
        bool allowed)
    {
        if (SequenceChecks.RawImage(definition, references) is not { } image || !NotStarting(machine, image))
        {
            return (false, null);
        }

        if (allowed)
        {
            return (true, null);
        }

        if (machine.SecureBootEnabled != true)
        {
            return (false, null);
        }

        return image.BootCapability == ImageBootCapability.SecureBootOk
            ? (false, ServerMessages.DeploymentUntrustedCaWithSecureBoot.With("image", image.Name, "ca", UefiCaName(image.SignedUnder)))
            : (false, ServerMessages.DeploymentNotStartingWithSecureBoot.With(
                "image",
                image.Name,
                "starting",
                BootCapabilities.NotStartingChoice(image.BootCapability)));
    }

    // The sentence the assignment's audit ends with when the run may write such an image.
    public static string MismatchNote(Machine machine, SequenceDefinition definition, SequenceReferences references, bool allowed)
    {
        if (!allowed || SequenceChecks.RawImage(definition, references) is not { } image || !NotStarting(machine, image))
        {
            return "";
        }

        return image.BootCapability == ImageBootCapability.SecureBootOk
            ? $" It may write {image.Name} although this machine's firmware does not trust {MicrosoftUefiCa.Describe(image.SignedUnder)}, which signed it."
            : $" It may write {image.Name} although it {BootCapabilities.NotStarting(image.BootCapability)} with Secure Boot on.";
    }

    // An image signed for Secure Boot does not start either where the firmware does not trust Microsoft's third-party
    // UEFI CA it is signed under.
    private static bool NotStarting(Machine machine, Image image) =>
        image.BootCapability != ImageBootCapability.SecureBootOk || MicrosoftUefiCa.Untrusted(machine.TrustedUefiCas, image.SignedUnder);

    // MicrosoftUefiCa.Describe as a message, for a sentence the web says.
    private static ServerMessage UefiCaName(UefiCa? cas) => ServerMessages.MicrosoftUefiCaName.With("cas", cas switch
    {
        UefiCa.Microsoft2011 => "ca2011",
        UefiCa.Microsoft2023 => "ca2023",
        UefiCa.Microsoft2011 | UefiCa.Microsoft2023 => "both",
        _ => "other",
    });
}
