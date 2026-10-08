// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Core.Boot;

// Writes the agent.json a boot environment puts beside the agent: the server's address and the root it trusts. The
// agent's format is frozen, so the names stay. Nothing in it is a secret.
public static class AgentConfigurationFile
{
    public const string Name = "agent.json";

    public static byte[] Write(Uri serverUrl, string rootCertificatePem, string? keyboardLayout)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCertificatePem);

        using MemoryStream file = new();

        using (Utf8JsonWriter writer = new(file))
        {
            writer.WriteStartObject();
            writer.WriteString("serverUrl", serverUrl.GetLeftPart(UriPartial.Authority));
            writer.WriteString("rootCertificate", rootCertificatePem);

            if (!string.IsNullOrWhiteSpace(keyboardLayout))
            {
                writer.WriteString("keyboardLayout", keyboardLayout);
            }

            writer.WriteEndObject();
        }

        file.WriteByte((byte)'\n');

        return file.ToArray();
    }
}
