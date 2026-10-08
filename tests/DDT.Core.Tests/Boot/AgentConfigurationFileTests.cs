// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Core.Tests.Boot;

public sealed class AgentConfigurationFileTests
{
    private const string Root = "-----BEGIN CERTIFICATE-----\nMIIB+zCCAaGg\n-----END CERTIFICATE-----\n";

    [Fact]
    public void WritesTheServersAddressTheRootAndTheLayoutUnderTheNamesTheAgentReads()
    {
        byte[] file = AgentConfigurationFile.Write(new Uri("https://ddt.example.test:8443"), Root, "German (de)");

        using JsonDocument document = JsonDocument.Parse(file);
        JsonElement root = document.RootElement;

        Assert.Equal(["serverUrl", "rootCertificate", "keyboardLayout"], root.EnumerateObject().Select(member => member.Name));
        Assert.Equal("https://ddt.example.test:8443", root.GetProperty("serverUrl").GetString());
        Assert.Equal(Root, root.GetProperty("rootCertificate").GetString());
        Assert.Equal("German (de)", root.GetProperty("keyboardLayout").GetString());
        Assert.Equal((byte)'\n', file[^1]);
    }

    [Fact]
    public void LeavesTheLayoutOutWhenThereIsNone()
    {
        byte[] file = AgentConfigurationFile.Write(new Uri("https://10.77.0.1:8443/"), Root, null);

        using JsonDocument document = JsonDocument.Parse(file);

        Assert.False(document.RootElement.TryGetProperty("keyboardLayout", out _));
        Assert.Equal("https://10.77.0.1:8443", document.RootElement.GetProperty("serverUrl").GetString());
    }

    // The file is handed to every machine that asks, so it may hold nothing but these three.
    [Fact]
    public void HoldsOnlyAsciiSoEveryReaderAgreesOnIt()
    {
        byte[] file = AgentConfigurationFile.Write(new Uri("https://ddt.example.test:8443"), Root, "Français");

        Assert.True(Ascii.IsValid(file));
        Assert.Contains("Fran\\u00E7ais", Encoding.ASCII.GetString(file), StringComparison.Ordinal);
    }
}
