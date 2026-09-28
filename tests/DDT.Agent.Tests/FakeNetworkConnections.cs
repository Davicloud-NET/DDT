// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Stands in for mpr.dll. It keeps the connections in a list and records every call except passwords. A connection from
// before the step, passed to the constructor, makes the first Add to the same server answer 1219 until it's cancelled.
internal sealed class FakeNetworkConnections : INetworkConnections
{
    private readonly List<string> _existing;
    private readonly List<string> _connected = [];

    public FakeNetworkConnections(params string[] existing) => _existing = [.. existing];

    public List<string> Calls { get; } = [];

    // The name whose Add fails, with Error.
    public string? AddError { get; set; }

    public NetworkError Error { get; set; } = new(67, "The network name cannot be found.");

    public NetworkError? Add(string remoteName, string userName, string password)
    {
        Calls.Add($"add {remoteName} as {userName}");

        if (remoteName == AddError)
        {
            return Error;
        }

        if (_existing.Exists(name => Same(ShareConnector.Host(name), ShareConnector.Host(remoteName))))
        {
            return new NetworkError(ShareConnector.SessionCredentialConflict, "Multiple connections to a server are not allowed.");
        }

        _connected.Add(remoteName);

        return null;
    }

    public NetworkError? Cancel(string remoteName)
    {
        Calls.Add($"cancel {remoteName}");
        _existing.RemoveAll(name => Same(name, remoteName));
        _connected.RemoveAll(name => Same(name, remoteName));

        return null;
    }

    public IReadOnlyList<string> Connected()
    {
        Calls.Add("enum");

        return [.. _existing, .. _connected];
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
