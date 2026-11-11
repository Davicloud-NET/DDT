// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Applications;

namespace DDT.Server.Applications;

// Reads and writes the JSON columns of applications and bundles.
internal static class ApplicationColumns
{
    public static IReadOnlyList<Guid> ReadIds(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, DdtJsonContext.Default.IReadOnlyListGuid) ?? [];

    public static string WriteIds(IReadOnlyList<Guid> ids) => JsonSerializer.Serialize(ids, DdtJsonContext.Default.IReadOnlyListGuid);

    public static IReadOnlyList<int> ReadCodes(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, DdtJsonContext.Default.IReadOnlyListInt32) ?? [];

    public static string WriteCodes(IReadOnlyList<int> codes) => JsonSerializer.Serialize(codes, DdtJsonContext.Default.IReadOnlyListInt32);

    public static ApplicationDetection? ReadDetection(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize(json, DdtJsonContext.Default.ApplicationDetection);

    public static string? WriteDetection(ApplicationDetection? detection) =>
        detection is null ? null : JsonSerializer.Serialize(detection, DdtJsonContext.Default.ApplicationDetection);
}
