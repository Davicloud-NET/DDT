// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// A late-bound call that failed. Code is Windows Update's own error, taken from EXCEPINFO when Invoke had one.
public sealed class ComCallException : Exception
{
    public ComCallException()
    {
    }

    public ComCallException(string message)
        : base(message)
    {
    }

    public ComCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ComCallException(string member, int code, string? description)
        : base(Describe(member, code, description))
    {
        Member = member;
        Code = code;
    }

    public string Member { get; } = "";

    public int Code { get; }

    public static string Describe(string member, int code, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? $"{member} ({UpdateNames.HResult(code)})"
            : $"{member} ({UpdateNames.HResult(code)}): {description.Trim().TrimEnd('.')}";
}
