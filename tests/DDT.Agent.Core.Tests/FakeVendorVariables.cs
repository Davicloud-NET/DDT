// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// A firmware's vendor variables in memory. Calls lists what was asked of it, in order. A call throws the failure set
// for it, as a firmware that refuses does.
internal sealed class FakeVendorVariables : IVendorVariables
{
    public Dictionary<(string Name, Guid Vendor), byte[]> Values { get; } = [];

    public List<string> Calls { get; } = [];

    public string? ReadFailure { get; set; }

    public string? WriteFailure { get; set; }

    public string? DeleteFailure { get; set; }

    // What the resume carrier's variable holds, or null.
    public byte[]? Resume
    {
        get => Values.GetValueOrDefault((ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        set
        {
            if (value is null)
            {
                Values.Remove((ResumeCarrier.VariableName, ResumeCarrier.Vendor));
            }
            else
            {
                Values[(ResumeCarrier.VariableName, ResumeCarrier.Vendor)] = value;
            }
        }
    }

    public byte[]? Read(string name, Guid vendor)
    {
        Act("read", name, ReadFailure);

        return Values.GetValueOrDefault((name, vendor));
    }

    public void Write(string name, Guid vendor, byte[] value)
    {
        Act("write", name, WriteFailure);
        Values[(name, vendor)] = value;
    }

    public void Delete(string name, Guid vendor)
    {
        Act("delete", name, DeleteFailure);
        Values.Remove((name, vendor));
    }

    private void Act(string call, string name, string? failure)
    {
        Calls.Add($"{call} {name}");

        if (failure is not null)
        {
            throw new DeploymentStepException(failure);
        }
    }
}
