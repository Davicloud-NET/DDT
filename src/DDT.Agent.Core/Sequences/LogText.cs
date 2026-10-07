// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

internal static class LogText
{
    // Matches the length the server keeps of a step's error.
    private const int MaxErrorLength = 1024;

    public static string OneLine(Exception exception)
    {
        string message = string.Join(' ', exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
    }

    // A phase's name in words.
    public static string Phase(string name) => name switch
    {
        nameof(SequencePhase.WindowsPE) => "Windows PE",
        nameof(SequencePhase.LinuxBoot) => "Linux boot environment",
        _ => name,
    };

    public static string Duration(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s" : $"{elapsed.TotalSeconds:0} s";
}
