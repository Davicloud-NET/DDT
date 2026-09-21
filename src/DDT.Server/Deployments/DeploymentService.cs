// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Core.Unattend;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Deployments;

// Which deployment a machine has and what may happen to it, for the web endpoints, the agent endpoints and the
// registrar alike. Nothing here saves. The caller saves the change together with its audit rows, so the
// concurrency tokens on Machine.State, TokenGeneration and ActiveDeploymentId settle every race between an
// assignment, a pick, a cancel, a report and a registration.
public sealed class DeploymentService(
    DdtDbContext database,
    UserManager<DdtUser> users,
    IOptions<MachineOptions> machineOptions,
    IOptions<DeploymentOptions> deploymentOptions,
    ZeroTouchNetworks zeroTouchNetworks,
    ListedProxies listedProxies,
    TimeProvider timeProvider)
{
    // The agent runs in x64 WinPE and starts bcdboot from the applied image, which fails for any other image.
    public const string DeployableArchitecture = "x64";

    private const string SomeOperator = "an operator";

    private const string CurrentStep = "Ask the server for the current deployment and report its current step.";

    public bool DomainConfigured => !string.IsNullOrWhiteSpace(deploymentOptions.Value.Domain.Name);

    // Under RequireWebApproval a netboot always waits for a sign-in, whatever networks are listed.
    public bool ZeroTouchEnabled => !machineOptions.Value.RequireWebApproval && !zeroTouchNetworks.IsEmpty;

    public async Task<Deployment?> ActiveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return machine.ActiveDeploymentId is { } id
            ? await database.Deployments.FindAsync([id], cancellationToken).ConfigureAwait(false)
            : null;
    }

    // What the Machines page shows: the active deployment, else the one that ended last.
    public async Task<Deployment?> ShownAsync(Machine machine, CancellationToken cancellationToken)
    {
        if (await ActiveAsync(machine, cancellationToken).ConfigureAwait(false) is { } active)
        {
            return active;
        }

        List<Deployment> deployments = await database.Deployments
            .Where(d => d.MachineId == machine.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Latest(deployments);
    }

    // One query for the whole list. SQLite cannot order by DateTimeOffset, so the latest is chosen here.
    public async Task<IReadOnlyDictionary<Guid, Deployment>> ShownForAsync(
        IReadOnlyCollection<Machine> machines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machines);

        Dictionary<Guid, Guid?> active = machines.ToDictionary(m => m.Id, m => m.ActiveDeploymentId);

        List<Deployment> deployments = await database.Deployments
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return deployments
            .Where(d => active.ContainsKey(d.MachineId))
            .GroupBy(d => d.MachineId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(d => d.Id == active[g.Key]) ?? Latest(g)!);
    }

    // Someone who may deploy signed in at this machine in its current token generation, and nothing is assigned.
    public async Task<bool> CanPickImageAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        cancellationToken.ThrowIfCancellationRequested();

        if (machine.State is not (MachineState.Approved or MachineState.Failed)
            || machine.SignedInByUserId is not { } userId
            || machine.ActiveDeploymentId is not null)
        {
            return false;
        }

        DdtUser? user = await users.FindByIdAsync(userId.ToString("D")).ConfigureAwait(false);

        return user is { IsDisabled: false }
            && !await users.IsLockedOutAsync(user).ConfigureAwait(false)
            && (await users.IsInRoleAsync(user, DdtRoleNames.Operator).ConfigureAwait(false)
                || await users.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<Image>> DeployableImagesAsync(CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images
            .AsNoTracking()
            .Where(i => i.Architecture == DeployableArchitecture)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. images.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id)];
    }

    public async Task<DeploymentDecision> AssignAsync(
        Machine machine,
        AssignImageRequest request,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        string? refusal = machine.State switch
        {
            MachineState.Deploying => "The machine is installing an image. Stop that deployment before assigning another image.",
            MachineState.Rejected => "The machine was rejected, so it cannot be given an image. Assign the image to another machine.",
            MachineState.Retired => "The machine was retired, so it cannot be given an image. Assign the image to another machine.",
            _ => null,
        };

        if (refusal is not null)
        {
            return DeploymentDecision.Conflict(refusal);
        }

        if (machine.ActiveDeploymentId is not null)
        {
            return DeploymentDecision.Conflict("This machine already has a deployment. Cancel it before assigning another image.");
        }

        Image? image = await database.Images.FindAsync([request.ImageId], cancellationToken).ConfigureAwait(false);

        if (image is null)
        {
            return DeploymentDecision.NotFound("The image no longer exists. Load the page again and choose another image.");
        }

        if (NotDeployable(image) is { } reason)
        {
            return DeploymentDecision.Conflict(reason);
        }

        // Nobody at the machine can say which disk to erase. A machine too old to report its disks is let through,
        // and its agent refuses the deployment itself if it finds more than one.
        if (machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict("This machine has more than one disk. Sign in at it and choose the disk there.");
        }

        if (ComputerNameProblem(machine, request.ComputerName) is { } problem)
        {
            return DeploymentDecision.Invalid("computerName", problem);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment deployment = Create(machine, image, DeploymentSource.Web, userId, userName, null, request.ComputerName, now);

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{image.Name} to machine {machine.Id:D}.",
            actorUserId: userId,
            actorName: userName));

        if (machine.State != MachineState.Pending)
        {
            // The tokens handed out with the Done report are of this generation, and Approved would accept them
            // again. The agent that held them rebooted, so nothing may use them any more.
            if (machine.State == MachineState.Done)
            {
                machine.TokenGeneration++;
            }

            machine.State = MachineState.Approved;
        }
        else if (AuthorizesWaitingMachine(machine, now))
        {
            machine.State = MachineState.Approved;
            machine.ApprovedByUserId = userId;
            machine.ApprovedUtc = now;
            machine.FirstApprovedUtc ??= now;

            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.MachineApproved,
                ActorUserId = userId,
                ActorName = userName,
                SubjectId = machine.Id.ToString("D"),
                SourceAddress = address,
                Detail = $"Was Pending. Approved by assigning {image.Name}.",
            });
        }

        return DeploymentDecision.Accepted(deployment);
    }

    // Chosen at the machine by whoever signed in there, who is also recorded as having requested it.
    public async Task<DeploymentDecision> PickAsync(
        Machine machine,
        AgentPickRequest request,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        if (!await CanPickImageAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return DeploymentDecision.Conflict(machine.ActiveDeploymentId is null
                ? "Only an operator or administrator signed in at this machine can choose an image. Start the machine from the network again and sign in."
                : "This machine already has a deployment. It starts once the agent asks the server again.");
        }

        Image? image = await database.Images.FindAsync([request.ImageId], cancellationToken).ConfigureAwait(false);

        if (image is null)
        {
            return DeploymentDecision.NotFound("The image no longer exists. Choose another image.");
        }

        if (NotDeployable(image) is { } reason)
        {
            return DeploymentDecision.Conflict(reason);
        }

        if (request.DiskNumber is < 0)
        {
            return DeploymentDecision.Invalid("diskNumber", "Choose one of the disks the agent listed.");
        }

        if (ComputerNameProblem(machine, request.ComputerName) is { } problem)
        {
            return DeploymentDecision.Invalid("computerName", problem);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment deployment = Create(
            machine,
            image,
            DeploymentSource.Console,
            machine.SignedInByUserId,
            machine.SignedInUserName,
            request.DiskNumber,
            request.ComputerName,
            now);

        machine.State = MachineState.Approved;

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{image.Name} to machine {machine.Id:D}, chosen at the machine.",
            actorUserId: machine.SignedInByUserId,
            actorName: machine.SignedInUserName,
            actorMachineId: machine.Id));

        return DeploymentDecision.Accepted(deployment);
    }

    // Cancels an assigned deployment, or stops a running one: the agent's next call is refused, and its resume
    // token no longer matches, so the machine starts over as Pending when it registers again.
    public async Task<DeploymentDecision> EndCurrentAsync(
        Machine machine,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        Deployment? active = await ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string by = userName ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. Cancelled by {by}.",
                    actorUserId: userId,
                    actorName: userName));

                return DeploymentDecision.Accepted(active);

            case DeploymentState.Running:
                string error = $"Stopped by {by}.";
                End(machine, active, DeploymentState.Failed, error, now);
                machine.State = MachineState.Failed;
                machine.TokenGeneration++;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. {error}",
                    actorUserId: userId,
                    actorName: userName));

                return DeploymentDecision.Accepted(active);

            default:
                return DeploymentDecision.Conflict(
                    "This machine has no deployment that is assigned or running. Load the page again to see its current state.");
        }
    }

    public void EndForRejection(Machine machine, Deployment? active, Guid? userId, string? userName, string? address)
    {
        ArgumentNullException.ThrowIfNull(machine);

        DateTimeOffset now = timeProvider.GetUtcNow();
        string by = userName ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. The machine was rejected by {by}.",
                    actorUserId: userId,
                    actorName: userName));
                break;

            case DeploymentState.Running:
                string error = $"Rejected by {by}.";
                End(machine, active, DeploymentState.Failed, error, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. {error}",
                    actorUserId: userId,
                    actorName: userName));
                break;
        }

        machine.ActiveDeploymentId = null;
    }

    // Under RequireWebApproval, a sign-in at a machine an operator already assigned an image on the web completes
    // the approval: the assignment was the web half.
    public static bool CountsAsWebApproval(Deployment? active) =>
        active is { State: DeploymentState.Assigned, Source: DeploymentSource.Web };

    // Zero touch: the web assignment authorizes the machine's next netboot, but only from a listed network. A request
    // still at a listed proxy's address carried no client address, so it proves nothing about the network.
    public bool KeepsApprovalOnNetboot(Deployment? active, IPAddress? remoteAddress) =>
        CountsAsWebApproval(active)
        && ZeroTouchEnabled
        && zeroTouchNetworks.Contains(remoteAddress)
        && !listedProxies.Contains(remoteAddress);

    // A registration without the resume token means the agent that had the deployment is gone. A running
    // deployment fails. An image chosen at the machine is cancelled: the disk and the ERASE typed there belonged
    // to that boot, and disk numbers can change across a restart. A web assignment stays for the next sign-in or
    // a zero touch netboot.
    public void EndForRestart(Machine machine, Deployment? active, string? address)
    {
        ArgumentNullException.ThrowIfNull(machine);

        DateTimeOffset now = timeProvider.GetUtcNow();

        switch (active)
        {
            case { State: DeploymentState.Running }:
                const string error = "The machine started again during the deployment.";
                End(machine, active, DeploymentState.Failed, error, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. {error}",
                    actorMachineId: machine.Id));
                break;

            case { State: DeploymentState.Assigned, Source: DeploymentSource.Console }:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.ImageName} on machine {machine.Id:D}. The machine started again before the image chosen at it was installed.",
                    actorMachineId: machine.Id));
                break;
        }
    }

    public async Task<DeploymentDecision> ReportAsync(
        Machine machine,
        Guid deploymentId,
        AgentDeploymentReport report,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        if (!Enum.IsDefined(report.Step))
        {
            return DeploymentDecision.Invalid("step", "The report names a step this server does not know. Use the agent this server provides.");
        }

        Deployment? deployment = await database.Deployments.FindAsync([deploymentId], cancellationToken).ConfigureAwait(false);

        if (deployment is null || deployment.MachineId != machine.Id)
        {
            return DeploymentDecision.NotFound("This machine has no such deployment. Ask the server for the current one.");
        }

        // The response to the last report can be lost, and the agent sends it again.
        if (deployment.State == report.State && deployment.State is DeploymentState.Done or DeploymentState.Failed)
        {
            return DeploymentDecision.Unchanged(deployment);
        }

        if (machine.ActiveDeploymentId != deployment.Id)
        {
            return DeploymentDecision.Conflict(
                $"The deployment is {Word(deployment.State)} and takes no further reports. Ask the server for the current one.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        int percent = Math.Clamp(report.Percent, 0, 100);

        switch (deployment.State, report.State)
        {
            case (DeploymentState.Assigned, DeploymentState.Running):
                deployment.State = DeploymentState.Running;
                deployment.StartedUtc = now;
                Progress(deployment, report.Step, percent, now);
                machine.State = MachineState.Deploying;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentStarted,
                    deployment,
                    now,
                    address,
                    $"{deployment.ImageName} on machine {machine.Id:D}.",
                    actorMachineId: machine.Id));
                break;

            case (DeploymentState.Running, DeploymentState.Running):
                if (report.Step < deployment.Step)
                {
                    return DeploymentDecision.Conflict(
                        $"The deployment is already at the {deployment.Step} step and cannot go back to the {report.Step} step. {CurrentStep}");
                }

                Progress(deployment, report.Step, percent, now);
                break;

            case (DeploymentState.Running, DeploymentState.Done):
                Progress(deployment, report.Step, percent, now);
                End(machine, deployment, DeploymentState.Done, null, now);
                machine.State = MachineState.Done;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentDone,
                    deployment,
                    now,
                    address,
                    $"{deployment.ImageName} on machine {machine.Id:D}.",
                    actorMachineId: machine.Id));
                break;

            // A check before the disk was touched failed. The contract makes the agent name a step, but none ran.
            case (DeploymentState.Assigned, DeploymentState.Failed):
                string refused = ErrorText(report.Error);
                End(machine, deployment, DeploymentState.Failed, refused, now);
                machine.State = MachineState.Failed;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    deployment,
                    now,
                    address,
                    $"{deployment.ImageName} on machine {machine.Id:D}: {refused}",
                    actorMachineId: machine.Id));
                break;

            case (DeploymentState.Running, DeploymentState.Failed):
                string error = ErrorText(report.Error);
                Progress(deployment, report.Step, percent, now);
                End(machine, deployment, DeploymentState.Failed, error, now);
                machine.State = MachineState.Failed;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    deployment,
                    now,
                    address,
                    $"{deployment.ImageName} on machine {machine.Id:D} at {report.Step}: {error}",
                    actorMachineId: machine.Id));
                break;

            default:
                return DeploymentDecision.Conflict(
                    $"A deployment that is {Word(deployment.State)} cannot be reported as {Word(report.State)}. {CurrentStep}");
        }

        return DeploymentDecision.Accepted(deployment);
    }

    // The answer file carries the deployment passwords, so only the machine's own running deployment gets it.
    public async Task<Deployment?> RunningAsync(Machine machine, Guid deploymentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? deployment = await database.Deployments.FindAsync([deploymentId], cancellationToken).ConfigureAwait(false);

        return deployment is { State: DeploymentState.Running } && deployment.MachineId == machine.Id ? deployment : null;
    }

    private static string Word(DeploymentState state) => state.ToString().ToLowerInvariant();

    private static Deployment? Latest(IEnumerable<Deployment> deployments) =>
        deployments.OrderByDescending(d => d.CreatedUtc).ThenByDescending(d => d.Id).FirstOrDefault();

    private static string? NotDeployable(Image image) => image.Architecture switch
    {
        DeployableArchitecture => null,
        null => $"{image.Name} does not say which processor it is for, and DDT deploys only x64 Windows. Choose an x64 image.",
        string architecture => $"{image.Name} is an {architecture} image, and DDT deploys only x64 Windows. Choose an x64 image.",
    };

    // A domain machine joins under its name, so it needs one. Without a domain, Setup makes one up.
    private string? ComputerNameProblem(Machine machine, string? computerName)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return ComputerNames.IsValid(computerName.Trim(), out string error) ? null : error;
        }

        return DomainConfigured && string.IsNullOrWhiteSpace(machine.AssignedName)
            ? "Enter a computer name. The machine joins the domain under this name."
            : null;
    }

    // Off: only a machine seen moments ago is at the prompt now; whoever holds the tokens of one seen earlier may
    // not be. On: the sign-in at the machine happened already, and the assignment is the web approval.
    private bool AuthorizesWaitingMachine(Machine machine, DateTimeOffset now) =>
        machineOptions.Value.RequireWebApproval
            ? machine.SignedInByUserId is not null
            : now - machine.LastSeenUtc <= DeploymentLimits.WaitingAtPrompt;

    private Deployment Create(
        Machine machine,
        Image image,
        DeploymentSource source,
        Guid? requestedByUserId,
        string? requestedByName,
        int? diskNumber,
        string? computerName,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            machine.AssignedName = computerName.Trim();
        }

        Deployment deployment = new()
        {
            Id = Guid.CreateVersion7(now),
            MachineId = machine.Id,
            ImageId = image.Id,
            ImageName = image.Name,
            Sha256 = image.Sha256,
            SizeBytes = image.SizeBytes,
            WimIndex = image.WimIndex,
            InstalledBytes = image.InstalledBytes,
            DiskNumber = diskNumber,
            State = DeploymentState.Assigned,
            Source = source,
            RequestedByUserId = requestedByUserId,
            RequestedByName = requestedByName,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        database.Deployments.Add(deployment);
        machine.ActiveDeploymentId = deployment.Id;

        return deployment;
    }

    private static void Progress(Deployment deployment, DeploymentStep step, int percent, DateTimeOffset now)
    {
        deployment.Step = step;
        deployment.Percent = percent;
        deployment.UpdatedUtc = now;
    }

    private static void End(Machine machine, Deployment deployment, DeploymentState state, string? error, DateTimeOffset now)
    {
        deployment.State = state;
        deployment.Error = error;
        deployment.FinishedUtc = now;
        deployment.UpdatedUtc = now;
        machine.ActiveDeploymentId = null;
    }

    // PostgreSQL text cannot hold a NUL, and a report it refuses would be resent forever.
    private static string ErrorText(string? error)
    {
        string text = (error ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        if (text.Length == 0)
        {
            return "The agent reported a failure without saying why.";
        }

        return text.Length <= DeploymentLimits.MaxErrorLength ? text : text[..DeploymentLimits.MaxErrorLength];
    }

    private static AuditEvent Audit(
        string action,
        Deployment deployment,
        DateTimeOffset now,
        string? address,
        string detail,
        Guid? actorUserId = null,
        string? actorName = null,
        Guid? actorMachineId = null) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = actorUserId,
            ActorMachineId = actorMachineId,
            ActorName = actorName,
            SubjectId = deployment.Id.ToString("D"),
            SourceAddress = address,
            Detail = detail,
        };
}
