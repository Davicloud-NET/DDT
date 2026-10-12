// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What a seed's user-data means for a run that goes on in the installed Linux. The agent gets there as a vendor-data
// script, which user-data can keep cloud-init from running. Only warnings and notes: the scan is no YAML reader.
internal static class UserDataRules
{
    private const string Field = "userData";

    // The content type cloud-init gives a script that begins with #!, as DDT's vendor data does.
    private const string ShellScript = "text/x-shellscript";

    // What cloud-init's is_true takes, of a scalar or its text.
    private static readonly string[] s_true = ["true", "yes", "on", "1"];

    // YAML's own false. cloud-init runs any other condition as a command.
    private static readonly string[] s_false = ["false", "no", "off"];

    // Both spellings name the module that runs vendor scripts: Ubuntu's cloud.cfg has the first, Debian's the second.
    private static readonly string[] s_vendorScripts = ["scripts_vendor", "scripts-vendor"];

    // What stands between the names in a list, written in block or in flow style.
    private static readonly char[] s_between = [' ', '\n', '\t', ',', '[', ']', '"', '\''];

    public static void Check(WriteCloudInitSeedStep seed, StepProblems problems)
    {
        string written = seed.UserData ?? "";

        // Text over the limit has a problem of its own, and none of it is read.
        if (Encoding.UTF8.GetByteCount(written) > SequenceValidator.MaxSeedFileBytes)
        {
            return;
        }

        // As the seed will hold it: CloudInitTemplate.Render writes every line end as a line feed.
        string userData = written.ReplaceLineEndings("\n");
        string text = userData.TrimStart('\uFEFF').TrimStart();

        // Nothing in an empty file switches anything off.
        if (text.Length == 0)
        {
            return;
        }

        CloudConfigScan? scan = CloudConfigScan.IsCloudConfig(userData) ? CloudConfigScan.Read(userData) : null;

        if (scan is not { Unreadable: false })
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataNotCheckedWarning.With());

            return;
        }

        CheckVendorData(scan["vendor_data"], problems);
        CheckFinalModules(scan["cloud_final_modules"], problems);
        CheckPowerState(scan["power_state"], problems);
    }

    // cloud-init merges a mapping into its own, where enabled is true. Anything but a mapping switches vendor data off.
    private static void CheckVendorData(CloudConfigValue? vendor, StepProblems problems)
    {
        if (vendor is null)
        {
            return;
        }

        CloudConfigMapping? members = vendor.Unsure ? null : vendor.Mapping;
        CloudConfigValue? enabled = members?["enabled"];

        string? how = vendor.Unsure || members is { Unsure: true } || enabled is { Unsure: true } ? "unread"
            : members is null ? "notMapping"
            : enabled is not null && !Is(enabled, s_true) ? "notEnabled"
            : null;

        if (how is not null)
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataVendorDataWarning.With("how", how));

            return;
        }

        if (members?["prefix"] is { IsEmpty: false })
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataVendorPrefixWarning.With());
        }

        CheckHandlers(members?["disabled_handlers"], problems);
    }

    // cloud-init hands vendor data of a content type listed here to no handler. Of a text it takes any part, of a
    // mapping the keys.
    private static void CheckHandlers(CloudConfigValue? handlers, StepProblems problems)
    {
        if (handlers is null)
        {
            return;
        }

        string[] words = handlers.Text.Split([.. s_between, '{', '}', ':'], StringSplitOptions.RemoveEmptyEntries);
        bool named = handlers.Scalar?.Contains(ShellScript, StringComparison.Ordinal)
            ?? words.Contains(ShellScript, StringComparer.Ordinal);

        // An escape in double quotes can spell the type in a way the text here does not show.
        if (handlers.Unsure || handlers.Text.Contains('\\', StringComparison.Ordinal))
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataVendorHandlersWarning.With("how", "unread"));
        }
        else if (named)
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataVendorHandlersWarning.With("how", "named"));
        }
    }

    // A list replaces cloud-init's own, so one without the module runs no vendor script.
    private static void CheckFinalModules(CloudConfigValue? modules, StepProblems problems)
    {
        if (modules is null)
        {
            return;
        }

        string[] words = modules.Text.Split(s_between, StringSplitOptions.RemoveEmptyEntries);

        if (modules.Unsure || modules.Text.Length == 0)
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataFinalModulesWarning.With("how", "unread"));
        }
        else if (!words.Any(word => s_vendorScripts.Contains(word, StringComparer.Ordinal)))
        {
            problems.Warn(Field, ServerMessages.SequenceUserDataFinalModulesWarning.With("how", "missing"));
        }
    }

    // The agent waits for the restart or the shutdown that cloud-init announces. A condition that is false cancels it.
    private static void CheckPowerState(CloudConfigValue? power, StepProblems problems)
    {
        if (power is null)
        {
            return;
        }

        CloudConfigMapping? members = power.Unsure ? null : power.Mapping;
        CloudConfigValue? condition = members?["condition"];

        if (condition is { Unsure: false } && Is(condition, s_false))
        {
            return;
        }

        // A merged mapping may bring a condition of its own.
        bool happens = members is { Unsure: false } && (condition is null || Is(condition, s_true));
        string mode = happens ? members?["mode"]?.Scalar?.ToLowerInvariant() ?? "" : "";

        problems.Note(Field, ServerMessages.SequenceUserDataPowerStateNote.With("mode", mode switch
        {
            "reboot" => "reboot",
            "poweroff" or "halt" => "off",
            _ => "unread",
        }));
    }

    private static bool Is(CloudConfigValue value, string[] words) =>
        value.Scalar is { } scalar && words.Contains(scalar.Trim(), StringComparer.OrdinalIgnoreCase);
}
