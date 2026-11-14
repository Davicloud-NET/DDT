// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.Servicing;

// What the step's DISM calls added and left out, and the build Windows had before the first update. DISM ends with 0
// for an update older than the image, so only the build says whether an update changed anything.
public sealed class ServicingTally
{
    private readonly List<string> _added = [];
    private readonly List<string> _leftOut = [];
    private readonly List<string> _updates = [];
    private bool _updateRan;

    // Null when no update ran, or the first one printed no version.
    public Version? Before { get; private set; }

    public static string Build(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return string.Create(CultureInfo.InvariantCulture, $"{version.Build}.{version.Revision}");
    }

    public void Count(ServicingCall call, ServicingAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(answer);

        if (call.IsUpdate && !_updateRan)
        {
            _updateRan = true;
            Before = answer.ImageVersion;
        }

        bool added = answer.Outcome == ServicingOutcome.Added;
        (added ? _added : _leftOut).Add(call.Names);

        if (added && call.IsUpdate)
        {
            _updates.Add(call.Names);
        }
    }

    // after is the image's version once every call is done, null when unknown.
    public void Report(AgentLog log, Version? after)
    {
        ArgumentNullException.ThrowIfNull(log);

        Version? before = _updates.Count > 0 ? Before : null;
        bool unchanged = before is not null && before == after;
        List<string> added = unchanged ? [.. _added.Where(names => !_updates.Contains(names, StringComparer.Ordinal))] : _added;

        log.Information(added.Count == 0 ? "No Windows package was added." : $"Added {string.Join("; ", added)}.");

        if (_leftOut.Count > 0)
        {
            log.Warning($"Left out, as they don't fit this Windows: {string.Join("; ", _leftOut)}.");
        }

        if (before is null || after is null)
        {
            return;
        }

        if (unchanged)
        {
            log.Warning($"Windows stays at build {Build(after)}: {string.Join("; ", _updates)} changed nothing. It is older than this Windows or made for another.");
        }
        else
        {
            log.Information($"Windows is at build {Build(after)} now, was {Build(before)}.");
        }
    }
}
