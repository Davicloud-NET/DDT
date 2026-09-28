// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Security;
using DDT.Server.Settings;

namespace DDT.Server.Deployments;

// What the settings let a run and a waiting machine do. A decision that reads settings takes the snapshot from its
// caller.
public static class DeploymentPolicy
{
    // The agent runs in x64 WinPE and starts bcdboot from the applied image, which fails for any other image.
    public const string DeployableArchitecture = "x64";

    public static bool IsDomainConfigured(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return !string.IsNullOrWhiteSpace(snapshot.Deployment.Domain.Name);
    }

    // Under RequireWebApproval, a sign-in at a machine an operator assigned a sequence on the web completes the approval.
    // A rule's run never counts, only a person's assignment.
    public static bool CountsAsWebApproval(Deployment? active) =>
        active is { State: DeploymentState.Assigned, Source: DeploymentSource.Web };

    // Zero touch keeps a web assignment's approval only on a netboot from a listed network. A request still at a listed
    // proxy's address carried no client address, so it proves nothing about the network.
    public static bool KeepsApprovalOnNetboot(SettingsSnapshot snapshot, Deployment? active, IPAddress? remoteAddress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return CountsAsWebApproval(active)
            && snapshot.Machines.ZeroTouchEnabled
            && snapshot.Machines.ZeroTouchNetworks.Contains(remoteAddress)
            && !ListedProxies.Contains(snapshot, remoteAddress);
    }

    // Off: only a machine seen moments ago is at the prompt now, whoever holds the tokens of one seen earlier may not be.
    // On: the sign-in at the machine happened already, and the assignment is the web approval.
    public static bool AuthorizesWaitingMachine(SettingsSnapshot snapshot, Machine machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(machine);

        return snapshot.Machines.RequireWebApproval
            ? machine.SignedInByUserId is not null
            : now - machine.LastSeenUtc <= DeploymentLimits.WaitingAtPrompt;
    }

    // A raw disk image whose boot file DDT could not read may still start, so only a known other processor keeps it
    // from being written.
    internal static ServerMessage? NotDeployable(Image image) => (image.Kind, image.Architecture) switch
    {
        (_, DeployableArchitecture) => null,
        (ImageKind.RawDisk, null) => null,
        (ImageKind.RawDisk, string architecture) =>
            ServerMessages.ImageRawForOtherArchitecture.With("image", image.Name, "architecture", architecture),
        (_, null) => ServerMessages.ImageWithoutArchitecture.With("image", image.Name),
        (_, string architecture) => ServerMessages.ImageOtherArchitecture.With("image", image.Name, "architecture", architecture),
    };

    // While the stored deployment settings have problems no run starts, since every run would carry values nobody checked.
    // Runs that started already keep the values they started with.
    public static ServerMessage? SettingsProblem(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.DeploymentProblems.Count == 0
            ? null
            : ServerMessages.DeploymentSettingsHaveProblems.With(
                "problems",
                ServerMessages.Sentences([.. snapshot.DeploymentProblems.Select(problem => ServerMessages.SettingsFieldProblem.With(
                    "field",
                    SettingsDefinitions.Deployment.PageName(problem.Field),
                    "problem",
                    problem.Text))]));
    }
}
