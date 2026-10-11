// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Json;

namespace DDT.Agent.LinuxPhase;

// A restart or power-off the administrator's user-data asked for, which comes after cloud-init has finished. Within is
// how long it may take to come.
public sealed record AnnouncedRestart(string What, TimeSpan Within)
{
    // A power_state's condition can call the restart off, and nothing says so. So the wait has an end.
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    // cloud-init's default for power_state's timeout.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // For a scheduled shutdown that does not say when.
    public static readonly TimeSpan UnknownDelay = TimeSpan.FromMinutes(5);

    // A week, so a made-up number cannot overflow the wait.
    private const double MaxMinutes = 7 * 24 * 60;
    private const double MaxSeconds = MaxMinutes * 60;

    // configuration is cloud-init's merged cloud-config, scheduled is systemd's note of a shutdown. Null without either
    // sign.
    public static AnnouncedRestart? Read(string? configuration, string? scheduled, DateTimeOffset now) =>
        OfPowerState(configuration) ?? OfSchedule(scheduled, now);

    private static AnnouncedRestart? OfPowerState(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(configuration);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("power_state", out JsonElement state)
                || state.ValueKind != JsonValueKind.Object
                || (state.TryGetProperty("condition", out JsonElement condition) && condition.ValueKind == JsonValueKind.False))
            {
                return null;
            }

            string mode = state.TryGetProperty("mode", out JsonElement named) && named.ValueKind == JsonValueKind.String ? named.GetString() ?? "" : "";
            TimeSpan timeout = Number(state, "timeout", MaxSeconds) is { } seconds ? TimeSpan.FromSeconds(seconds) : DefaultTimeout;

            return new AnnouncedRestart($"power_state: {(mode.Length > 0 ? mode : "no mode")}", Delay(state) + timeout + Margin);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The delay is in minutes: a number, "+5", or "now".
    private static TimeSpan Delay(JsonElement state)
    {
        if (Number(state, "delay", MaxMinutes) is { } minutes)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        return state.TryGetProperty("delay", out JsonElement delay)
            && delay.ValueKind == JsonValueKind.String
            && double.TryParse(delay.GetString()?.TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out double named)
            && named is >= 0 and <= MaxMinutes
                ? TimeSpan.FromMinutes(named)
                : TimeSpan.Zero;
    }

    private static double? Number(JsonElement state, string name, double most) =>
        state.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && number >= 0 && number <= most
            ? number
            : null;

    // systemd writes USEC, the time in microseconds since 1970, and MODE.
    private static AnnouncedRestart? OfSchedule(string? scheduled, DateTimeOffset now)
    {
        if (scheduled is null)
        {
            return null;
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string line in scheduled.Split('\n'))
        {
            int equals = line.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }

        string mode = values.GetValueOrDefault("MODE") is { Length: > 0 } named ? named : "shutdown";
        TimeSpan delay = values.TryGetValue("USEC", out string? usec) && long.TryParse(usec, NumberStyles.None, CultureInfo.InvariantCulture, out long microseconds)
            ? TimeSpan.FromSeconds(Math.Clamp((microseconds / 1_000_000d) - (now.ToUnixTimeMilliseconds() / 1000d), 0, MaxSeconds))
            : UnknownDelay;

        return new AnnouncedRestart($"a scheduled {mode}", delay + Margin);
    }
}
