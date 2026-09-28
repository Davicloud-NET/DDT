// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Server.Settings;

public sealed class SettingField
{
    public SettingField(
        string path,
        SettingFieldKind kind = SettingFieldKind.Value,
        bool reauthenticate = false,
        bool seeds = false,
        IReadOnlyList<string>? entryMembers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        Kind = kind;
        Reauthenticate = reauthenticate;
        Seeds = seeds;
        EntryMembers = entryMembers ?? [];
        Segments = [.. path.Split(':').Select(Camel)];
        Name = string.Join('.', Segments);
    }

    // The configuration key below the section, such as Domain:Name.
    public string Path { get; }

    // Path in camel case, such as domain.name, so the page and the stored document name the field alike.
    public string Name { get; }

    // Where the stored document and the serialized options keep it.
    public IReadOnlyList<string> Segments { get; }

    public SettingFieldKind Kind { get; }

    // A change needs a fresh proof of the administrator's identity, because the field grants roles or trust.
    public bool Reauthenticate { get; }

    public bool IsSecret => Kind == SettingFieldKind.Secret;

    // Configuration only seeds it: its key is imported while the field was never written, but it never locks the field,
    // because the key means something of its own that stays in configuration.
    public bool Seeds { get; }

    // The members of a map's entries where an entry is an object, such as a boot target's Method, by their names in
    // configuration; empty where an entry is a single value, such as the role of a group in a group map.
    public IReadOnlyList<string> EntryMembers { get; }

    public override string ToString() => Name;

    // As the source generated context names the members of the options, so both agree on every name.
    internal static string Camel(string segment) => JsonNamingPolicy.CamelCase.ConvertName(segment);
}
