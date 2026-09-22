// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Machines;

public sealed class MachineTokenService(IDataProtectionProvider dataProtectionProvider, TimeProvider timeProvider)
{
    private const string PurposeRoot = "DDT.MachineToken";

    public string Issue(Machine machine, MachineTokenPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(machine);

        MachineTokenPayload payload = new(
            machine.Id,
            machine.SmbiosUuid,
            machine.PrimaryMac,
            machine.TokenGeneration);

        string json = JsonSerializer.Serialize(payload, MachineTokenJsonContext.Default.MachineTokenPayload);

        return Protector(purpose).ToTimeLimitedDataProtector().Protect(json, LifetimeFor(purpose));
    }

    public MachineTokenPayload? Validate(string token, MachineTokenPurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || purpose == MachineTokenPurpose.Run)
        {
            return null;
        }

        return Unprotect(token, Protector(purpose).ToTimeLimitedDataProtector(), MachineTokenJsonContext.Default.MachineTokenPayload);
    }

    // For one run of one machine in its current generation. It is not rotated: an agent that lost the answer to the
    // registration that handed out a new one still holds a token that works.
    public string IssueRunToken(Machine machine, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(machine);

        RunTokenPayload payload = new(machine.Id, runId, machine.TokenGeneration, timeProvider.GetUtcNow() + MachineTokenLifetimes.Run);

        return Protector(MachineTokenPurpose.Run).Protect(JsonSerializer.Serialize(payload, MachineTokenJsonContext.Default.RunTokenPayload));
    }

    // Null when the token is not one or has expired. Whether its run still runs is for the caller to check.
    public RunTokenPayload? ValidateRunToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        RunTokenPayload? payload = Unprotect(token, Protector(MachineTokenPurpose.Run), MachineTokenJsonContext.Default.RunTokenPayload);

        return payload is not null && payload.ExpiresUtc > timeProvider.GetUtcNow() ? payload : null;
    }

    private static T? Unprotect<T>(string token, IDataProtector protector, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(protector.Unprotect(token), typeInfo);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IDataProtector Protector(MachineTokenPurpose purpose) =>
        dataProtectionProvider.CreateProtector(PurposeRoot, purpose.ToString());

    private static TimeSpan LifetimeFor(MachineTokenPurpose purpose) => purpose switch
    {
        MachineTokenPurpose.Poll => MachineTokenLifetimes.Poll,
        MachineTokenPurpose.Session => MachineTokenLifetimes.Session,
        MachineTokenPurpose.Resume => MachineTokenLifetimes.Resume,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };
}
