// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Agent.Tests;

// The server writes agent.json into the Linux boot environment's site.cpio with AgentConfigurationFile, and the agent
// reads it with its own reader. Neither side may drift from the other.
public sealed class ServerWrittenConfigurationTests
{
    [Fact]
    public void TheAgentReadsTheAgentJsonTheServerWrites()
    {
        using X509Certificate2 root = TestCertificates.Root("DDT root 1a2b3c4d");
        string path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, AgentConfigurationFile.Write(new Uri("https://ddt-server.example.test:8443"), root.ExportCertificatePem(), "German (de)"));

        try
        {
            Assert.True(AgentOptions.TryParse(["--config", path], out AgentOptions? options, out string error), error);
            Assert.Equal(new Uri("https://ddt-server.example.test:8443"), options!.ServerUrl);
            Assert.Equal(root.RawData, options.RootCertificate!.RawData);
            Assert.Equal("German (de)", options.KeyboardLayout);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileWithoutALayoutLeavesTheAgentWithoutOne()
    {
        using X509Certificate2 root = TestCertificates.Root("DDT root 5e6f7a8b");
        string path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, AgentConfigurationFile.Write(new Uri("https://10.77.0.1:8443"), root.ExportCertificatePem(), null));

        try
        {
            Assert.True(AgentOptions.TryParse(["--config", path], out AgentOptions? options, out string error), error);
            Assert.Equal(new Uri("https://10.77.0.1:8443"), options!.ServerUrl);
            Assert.Null(options.KeyboardLayout);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
