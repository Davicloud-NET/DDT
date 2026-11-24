// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Autopilot;

namespace DDT.Server.Tests;

// Stands in for Microsoft Graph in every server test, so none reaches Microsoft. Each call takes the next scripted
// answer, which may throw; without one it throws InvalidOperationException. Calls holds "import <serial>" and
// "read <id>".
public sealed class FakeAutopilotDirectory : IAutopilotDirectory
{
    private readonly Lock _lock = new();
    private readonly Queue<Func<AutopilotImport>> _answers = new();
    private readonly List<string> _calls = [];
    private readonly List<AutopilotDevice> _devices = [];

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public List<AutopilotDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    public FakeAutopilotDirectory Then(Func<AutopilotImport> answer)
    {
        lock (_lock)
        {
            _answers.Enqueue(answer);
        }

        return this;
    }

    public FakeAutopilotDirectory Then(string id, AutopilotImportStatus status, string? error = null) =>
        Then(() => new AutopilotImport(id, status, error));

    public void Reset()
    {
        lock (_lock)
        {
            _answers.Clear();
            _calls.Clear();
            _devices.Clear();
        }
    }

    public Task<AutopilotImport> ImportAsync(AutopilotOptions account, AutopilotDevice device, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _devices.Add(device);
        }

        return Answer($"import {device.SerialNumber}");
    }

    public Task<AutopilotImport> ReadAsync(AutopilotOptions account, string importId, CancellationToken cancellationToken) => Answer($"read {importId}");

    private Task<AutopilotImport> Answer(string call)
    {
        Func<AutopilotImport>? answer;

        lock (_lock)
        {
            _calls.Add(call);
            _answers.TryDequeue(out answer);
        }

        try
        {
            return Task.FromResult((answer ?? throw new InvalidOperationException($"No scripted answer for {call}."))());
        }
        catch (Exception exception)
        {
            return Task.FromException<AutopilotImport>(exception);
        }
    }
}
