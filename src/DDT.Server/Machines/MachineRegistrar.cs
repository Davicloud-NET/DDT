using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

public sealed partial class MachineRegistrar(
    DdtDbContext database,
    MachineTokenService tokens,
    LiveNotifier live,
    IOptions<MachineOptions> options,
    TimeProvider timeProvider,
    ILogger<MachineRegistrar> logger)
{
    public const int PollAfterSeconds = 10;

    private const int MaxAttempts = 3;

    // Null when a machine the server has not seen before is refused, because too many are already waiting.
    public async Task<AgentRegistrationResult?> RegisterAsync(
        NormalisedRegistration registration,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TryRegisterAsync(registration, address, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // An approval, rejection or other registration changed the machine in between. Decide again
                // from what is stored now rather than overwrite it.
                database.ChangeTracker.Clear();
            }
        }
    }

    // The issued tokens are the machine's own tokens for its current state, as NextAsync hands out.
    public string CurrentToken(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return tokens.Issue(
            machine,
            machine.State is MachineState.Approved or MachineState.Deploying ? MachineTokenPurpose.Session : MachineTokenPurpose.Poll);
    }

    private async Task<AgentRegistrationResult?> TryRegisterAsync(
        NormalisedRegistration registration,
        string? address,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Machine? machine = await FindAsync(registration, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            if (await TooManyWaitingAsync(address, cancellationToken).ConfigureAwait(false))
            {
                LogTooManyWaiting(address ?? "unknown");

                return null;
            }

            machine = new Machine
            {
                Id = Guid.CreateVersion7(now),
                SmbiosUuid = registration.SmbiosUuid,
                PrimaryMac = registration.PrimaryMac,
                FirstSeenUtc = now,
                FirstSeenAddress = address,
            };

            database.Machines.Add(machine);
            database.AuditEvents.Add(Audit(now, AuditActions.MachineRegistered, machine, address));
        }
        else if (machine.State == MachineState.Rejected)
        {
            database.AuditEvents.Add(Audit(now, AuditActions.MachineReregistered, machine, address, "Still rejected."));
        }
        else if (!Resumes(registration, machine))
        {
            // Anyone who reaches the server can present a machine's UUID and MAC. Unless the registration
            // proves it comes from the agent already holding this machine, it starts over: any approval is
            // dropped and every token issued so far dies with the generation bump, so two agents can never
            // share one machine's tokens.
            database.AuditEvents.Add(Audit(now, AuditActions.MachineReregistered, machine, address, $"Was {machine.State}."));

            machine.State = MachineState.Pending;
            machine.TokenGeneration++;
            machine.ApprovedByUserId = null;
            machine.ApprovedUtc = null;
            machine.SignedInByUserId = null;
            machine.SignedInUserName = null;
            machine.SignedInUtc = null;
        }

        machine.PrimaryMac = registration.PrimaryMac;
        machine.MacAddresses = string.Join(',', registration.MacAddresses);
        machine.Manufacturer = registration.Manufacturer;
        machine.Model = registration.Model;
        machine.SerialNumber = registration.SerialNumber;
        machine.AgentVersion = registration.AgentVersion;
        machine.LastSeenUtc = now;
        machine.LastSeenAddress = address;

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        live.MachineChanged(machine);

        return machine.State == MachineState.Rejected
            ? new AgentRegistrationResult(machine.Id, machine.State, null, null, PollAfterSeconds, null)
            : new AgentRegistrationResult(
                machine.Id,
                machine.State,
                CurrentToken(machine),
                tokens.Issue(machine, MachineTokenPurpose.Resume),
                PollAfterSeconds,
                machine.SignedInUserName);
    }

    // Only machines nobody has approved count, so a fleet that was deployed and booted again never blocks a
    // new machine.
    private async Task<bool> TooManyWaitingAsync(string? address, CancellationToken cancellationToken)
    {
        IQueryable<Machine> waiting = database.Machines
            .Where(m => m.State == MachineState.Pending && m.FirstApprovedUtc == null);

        return await waiting.CountAsync(m => m.FirstSeenAddress == address, cancellationToken).ConfigureAwait(false) >= options.Value.MaxWaitingPerAddress
            || await waiting.CountAsync(cancellationToken).ConfigureAwait(false) >= options.Value.MaxWaiting;
    }

    private bool Resumes(NormalisedRegistration registration, Machine machine) =>
        registration.ResumeToken is { } presented
        && tokens.Validate(presented, MachineTokenPurpose.Resume) is { } payload
        && payload.MachineId == machine.Id
        && payload.TokenGeneration == machine.TokenGeneration;

    // A machine is the same one when the UUID matches exactly and at least one of its network adapters is
    // still present. A UUID alone is not enough: cloned virtual machines and some boards share one, and
    // SMBIOS reserves all zeros and all ones for "no UUID", which cheap firmware reports. Matching on the
    // exact UUID string also keeps a machine without a UUID from ever landing on one that has one.
    private async Task<Machine?> FindAsync(NormalisedRegistration registration, CancellationToken cancellationToken)
    {
        List<Machine> candidates = await database.Machines
            .Where(m => m.SmbiosUuid == registration.SmbiosUuid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Where(m => m.MacAddresses.Split(',').Intersect(registration.MacAddresses).Any())
            .OrderBy(m => m.FirstSeenUtc)
            .FirstOrDefault();
    }

    private static AuditEvent Audit(
        DateTimeOffset now,
        string action,
        Machine machine,
        string? address,
        string? detail = null) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorMachineId = machine.Id,
            SubjectId = machine.Id.ToString("D"),
            SourceAddress = address,
            Detail = detail,
        };

    [LoggerMessage(EventId = 410, Level = LogLevel.Warning, Message = "Refused a new machine from {Address}: too many machines nobody approved are waiting")]
    private partial void LogTooManyWaiting(string address);
}
