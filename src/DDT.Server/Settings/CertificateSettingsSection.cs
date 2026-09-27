// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Configuration;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// The names the server is reached by, which Generate issues for and which an uploaded certificate has to cover.
// DDT:Https:SubjectAlternativeNames seeds them once and never locks them: in configuration it also names what DDT's own
// certificate has to carry, which it reissues on its own, so the key stays there with that meaning.
public sealed class CertificateSettingsSection() : SettingsSectionDefinition<HttpsOptions>(
    SettingsSectionNames.Certificate,
    HttpsOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("SubjectAlternativeNames", reauthenticate: true, seeds: true),
    ])
{
    protected override JsonTypeInfo<HttpsOptions> TypeInfo => SettingsJsonContext.Default.HttpsOptions;

    protected override HttpsOptions? Bind(IConfigurationSection section) => section.Get<HttpsOptions>();

    protected override IReadOnlyList<SettingProblem> FindProblems(HttpsOptions options, SettingsContext context) =>
    [
        .. Names(options.SubjectAlternativeNames)
            .Where(name => Uri.CheckHostName(name) == UriHostNameType.Unknown)
            .Select(name => new SettingProblem("SubjectAlternativeNames", ServerMessages.SettingsCertificateNameInvalid.With("name", name))),
    ];

    public static IReadOnlyList<string> Names(string value) =>
        [.. (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
