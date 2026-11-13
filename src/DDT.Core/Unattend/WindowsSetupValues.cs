// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Core.Unattend;

// Shapes of answer file values that need no culture data, so the agent and the web's rules can check them too.
public static partial class WindowsSetupValues
{
    // Setup's limit for RegisteredOrganization and RegisteredOwner.
    public const int MaxRegisteredLength = 256;

    // The run value that overrides the Deployment defaults' product key.
    public const string ProductKeyValue = "ProductKey";

    // A language tag such as de-DE or sr-Latn-RS. Which languages an image has, only Setup knows.
    public static bool IsUiLanguage(string? value) => value is not null && LanguageTag().IsMatch(value.Trim());

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){1,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();
}
