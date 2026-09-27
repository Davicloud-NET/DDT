// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Agent.WindowsPhase;

// What DDT's session keeps in <Windows>\DDT\session.json, which only SYSTEM can open, from the hand-over in Windows PE
// to the end of the run: the name of the console's pipe, which the session's shell names too; the password the hand-over
// put into the answer file for Setup's sign-in, until the session is up and the password is a new one; and the machine's
// settings as they were before the session changed them, once the service has read them, to be put back at the end.
public sealed record DeploySessionFile(string PipeName, string? Password, IReadOnlyList<SavedSetting>? Saved);

// A registry value under HKEY_LOCAL_MACHINE as it was: a string, a number, or neither when there was none.
public sealed record SavedSetting(string Key, string Name, string? Text, int? Number);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeploySessionFile))]
public sealed partial class DeploySessionFileJsonContext : JsonSerializerContext;
