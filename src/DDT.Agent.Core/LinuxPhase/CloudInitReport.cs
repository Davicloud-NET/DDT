// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Agent.LinuxPhase;

// Puts into the run's log how cloud-init ended: its errors as warnings, and the end of what it printed. Nothing here
// fails the run, because the image's set-up is the administrator's and may be meant to end that way.
internal static class CloudInitReport
{
    public const int MaxWarnings = 20;

    public const string UnreadableResult = "cloud-init's result cannot be read, so the errors it ended with are not known.";

    private const int MaxMessageLength = 500;

    public static void Write(string result, string? status, IReadOnlyList<string> output, AgentLog log)
    {
        log.Information("cloud-init has finished at this start.");

        List<string> warnings = [.. Errors(result).Concat(Recovered(status)).Distinct(StringComparer.Ordinal)];

        foreach (string warning in warnings.Take(MaxWarnings))
        {
            log.Warning(warning);
        }

        if (warnings.Count > MaxWarnings)
        {
            log.Warning($"cloud-init reported {warnings.Count - MaxWarnings} more. cloud-init status --long lists them on the machine.");
        }

        if (output.Count == 0)
        {
            return;
        }

        log.Information($"The end of /{CloudInitFiles.OutputPath}:");

        foreach (string line in output)
        {
            log.Information($"cloud-init: {line}");
        }
    }

    // v1.errors of result.json, which is what made cloud-init end with an error.
    private static IEnumerable<string> Errors(string result)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(result);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("v1", out JsonElement v1)
                && v1.ValueKind == JsonValueKind.Object
                && v1.TryGetProperty("errors", out JsonElement errors)
                    ? [.. Texts(errors).Select(error => $"cloud-init ended with an error: {error}")]
                    : [];
        }
        catch (JsonException)
        {
            return [UnreadableResult];
        }
    }

    // recoverable_errors of each stage in status.json, by level. A user-data that is not YAML shows only here.
    private static IEnumerable<string> Recovered(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(status);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("v1", out JsonElement v1)
                || v1.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return [.. v1.EnumerateObject().SelectMany(stage => Recovered(stage.Value))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Several stages repeat the same line, so the stage is left out and each line is said once.
    private static IEnumerable<string> Recovered(JsonElement stage)
    {
        if (stage.ValueKind != JsonValueKind.Object
            || !stage.TryGetProperty("recoverable_errors", out JsonElement levels)
            || levels.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return levels.EnumerateObject().SelectMany(level => Texts(level.Value).Select(text => $"cloud-init {level.Name}: {text}"));
    }

    private static IEnumerable<string> Texts(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => Cut(item.GetString() ?? ""))
            : [];

    private static string Cut(string text)
    {
        string line = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return line.Length > MaxMessageLength ? line[..MaxMessageLength] : line;
    }
}
