// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Takes the machine's resume token across one restart into the other boot environment, in a UEFI variable of DDT's own.
public sealed class ResumeCarrier(IVendorVariables variables, AgentEnvironment environment, AgentLog log)
{
    // Frozen, like the layout below: the agent of one boot environment writes what the other one's reads.
    public const string VariableName = "DdtResume";

    // The layout: this byte, the boot environment the machine restarts into, then the resume token as ASCII.
    public const byte Version = 1;

    public const byte WindowsPECode = 1;

    public const byte LinuxBootCode = 2;

    private const int HeaderLength = 2;

    // The token is a few hundred characters, and a firmware keeps far less than a megabyte of variables.
    private const int MaxTokenLength = 4096;

    // DDT's own vendor GUID, made up once for this variable. Frozen too.
    public static readonly Guid Vendor = new("d34b9a64-3d20-47de-94c2-e70dfc2ecce0");

    // The machine was to restart into the other boot environment, and this one started again.
    public bool SwitchFailed { get; private set; }

    // Null for a token that is empty, too long or not ASCII, and for an installed system, which nothing restarts into.
    public static byte[]? Encode(AgentEnvironment target, string resumeToken)
    {
        ArgumentNullException.ThrowIfNull(resumeToken);

        if (Code(target) is not { } code || resumeToken.Length is 0 or > MaxTokenLength || !Ascii.IsValid(resumeToken))
        {
            return null;
        }

        byte[] payload = new byte[HeaderLength + resumeToken.Length];
        payload[0] = Version;
        payload[1] = code;
        Encoding.ASCII.GetBytes(resumeToken, payload.AsSpan(HeaderLength));

        return payload;
    }

    // Null for anything this version did not write.
    public static (AgentEnvironment Target, string ResumeToken)? Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= HeaderLength
            || payload.Length > HeaderLength + MaxTokenLength
            || payload[0] != Version
            || !Ascii.IsValid(payload[HeaderLength..]))
        {
            return null;
        }

        AgentEnvironment? target = payload[1] switch
        {
            WindowsPECode => AgentEnvironment.WindowsPE,
            LinuxBootCode => AgentEnvironment.LinuxBoot,
            _ => null,
        };

        return target is { } known ? (known, Encoding.ASCII.GetString(payload[HeaderLength..])) : null;
    }

    // The boot environment the variable says the machine restarts into, without taking it. Null when it names none.
    public static AgentEnvironment? TargetIn(IVendorVariables variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        try
        {
            return variables.Read(VariableName, Vendor) is { } payload ? Decode(payload)?.Target : null;
        }
        catch (DeploymentStepException)
        {
            return null;
        }
    }

    // False when the firmware did not take it. The machine then registers anew after the restart, and someone signs in
    // at it again.
    public bool Put(AgentEnvironment target, string resumeToken)
    {
        const string SignInAgain = "The machine registers anew after the restart, and someone signs in at it again.";

        if (Encode(target, resumeToken) is not { } payload)
        {
            log.Warning($"The server gave no resume token that can go into the firmware. {SignInAgain}");

            return false;
        }

        try
        {
            variables.Write(VariableName, Vendor, payload);

            return true;
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware did not keep what lets this machine stay signed in across the restart ({exception.Message}). {SignInAgain}");

            return false;
        }
    }

    // Reads the token an agent left before a restart and deletes it, before this agent registers. Null when there is
    // none. A token left for the other boot environment means that one did not start.
    public string? Take()
    {
        byte[]? payload;

        try
        {
            payload = variables.Read(VariableName, Vendor);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware cannot be asked whether this machine restarted for DDT ({exception.Message}). It registers anew.");

            return null;
        }

        if (payload is null)
        {
            return null;
        }

        Delete();

        if (Decode(payload) is not { } carried)
        {
            log.Warning($"The firmware variable {VariableName} holds nothing this agent can read, so it was deleted. The machine registers anew.");

            return null;
        }

        if (carried.Target != environment)
        {
            SwitchFailed = true;
            log.Warning(
                $"This machine restarted to start {AgentEnvironments.Name(carried.Target)}, and {AgentEnvironments.Name(environment)} started again: " +
                "the server could not start the other boot environment. The machine is not restarted for it again until it was started anew.");
        }

        return carried.ResumeToken;
    }

    // A restart the token was put for does not happen, so the next start must not find it.
    public void Withdraw() => Delete();

    private void Delete()
    {
        try
        {
            variables.Delete(VariableName, Vendor);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware variable {VariableName} could not be deleted ({exception.Message}). The next start of the agent finds it again.");
        }
    }

    private static byte? Code(AgentEnvironment target) => target switch
    {
        AgentEnvironment.WindowsPE => WindowsPECode,
        AgentEnvironment.LinuxBoot => LinuxBootCode,
        _ => null,
    };
}
