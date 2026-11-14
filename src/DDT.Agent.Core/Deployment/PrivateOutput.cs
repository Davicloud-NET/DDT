// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// What a tool that prints a secret wrote. The tool runner hands its lines here and never to the log.
public sealed class PrivateOutput
{
    private readonly List<string> _lines = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Lines => _lines;

    public IReadOnlyList<string> Errors => _errors;

    internal void Add(string line) => _lines.Add(line);

    internal void AddError(string line) => _errors.Add(line);

    internal void Clear()
    {
        _lines.Clear();
        _errors.Clear();
    }
}
