// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Signs accounts in and connects their shares without a real logon or server, in one journal, so a test sees the shares
// connect before a step and go after it, even when it fails, and the account sign out. Passwords are recorded nowhere.
internal sealed class FakeAccountTools
{
    private readonly List<string> _events = [];

    public FakeShareConnector Shares { get; }

    public FakeAccountLogons Logons { get; }

    public FakeAccountTools()
    {
        Shares = new FakeShareConnector(_events);
        Logons = new FakeAccountLogons(_events);
    }

    public AccountTools Tools => new(Shares, Logons);

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    internal void Record(string what)
    {
        lock (_events)
        {
            _events.Add(what);
        }
    }
}
