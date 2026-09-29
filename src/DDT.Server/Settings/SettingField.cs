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

    // Path in camel case, such as domain.name, so the page and the stored document use the same name.
    public string Name { get; }

    // The path where the stored document and the serialized options keep the field.
    public IReadOnlyList<string> Segments { get; }

    public SettingFieldKind Kind { get; }

    // A change needs a fresh proof of the administrator's identity, because the field grants roles or trust.
    public bool Reauthenticate { get; }

    public bool IsSecret => Kind == SettingFieldKind.Secret;

    // Configuration only seeds this field. Its key is imported as long as the field was never written, but it never
    // locks the field. The key has a meaning of its own that stays in configuration.
    public bool Seeds { get; }

    // The member names of a map's entries, as configuration spells them, when an entry is an object, such as a boot
    // target's Method. It's empty when an entry is a single value, such as the role of a group in a group map.
    public IReadOnlyList<string> EntryMembers { get; }

    public override string ToString() => Name;

    // Uses the same naming as the source generated context uses for the options' members, so both agree on every name.
    internal static string Camel(string segment) => JsonNamingPolicy.CamelCase.ConvertName(segment);
}
