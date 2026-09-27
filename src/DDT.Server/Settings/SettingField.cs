// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Server.Settings;

public enum SettingFieldKind
{
    // One value: a text, a number, a switch, or a list kept as comma separated text.
    Value,

    // A map or a list, which configuration sets and locks as a whole, never entry by entry.
    Collection,

    // Written, never read back. It is stored encrypted, and the page only learns whether it is set.
    Secret,
}

// One field of a section. Path is its configuration key below the section, such as Domain:Name. The page and the stored
// document both name it in camel case, domain.name, so that a field reads the same everywhere but in configuration.
public sealed class SettingField
{
    public SettingField(string path, SettingFieldKind kind = SettingFieldKind.Value, bool reauthenticate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        Kind = kind;
        Reauthenticate = reauthenticate;
        Segments = [.. path.Split(':').Select(Camel)];
        Name = string.Join('.', Segments);
    }

    public string Path { get; }

    public string Name { get; }

    // Where the stored document and the serialized options keep it.
    public IReadOnlyList<string> Segments { get; }

    public SettingFieldKind Kind { get; }

    // A change needs a fresh proof of the administrator's identity, because the field grants roles or trust.
    public bool Reauthenticate { get; }

    public bool IsSecret => Kind == SettingFieldKind.Secret;

    public override string ToString() => Name;

    // As the source generated context names the members of the options, so both agree on every name.
    internal static string Camel(string segment) => JsonNamingPolicy.CamelCase.ConvertName(segment);
}
