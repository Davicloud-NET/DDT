// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using Xunit;

namespace DDT.Agent.Tests;

// The applications an Install applications step gets from the server, as the agent reads them.
public sealed class AgentApplicationContractTests
{
    private const string MachineId = "0197a3c0-0000-7000-8000-00000000000a";
    private const string RunId = "0197a3c0-0000-7000-8000-00000000000b";
    private const string StepId = "0197a3c0-0000-7000-8000-00000000000c";
    private const string ApplicationId = "0197a3c0-0000-7000-8000-00000000000d";
    private const string DependencyId = "0197a3c0-0000-7000-8000-00000000000e";

    private static AgentApplication Application(ApplicationDetection? detection) => new(
        Guid.Parse(ApplicationId),
        "7-Zip",
        ApplicationKind.Command,
        "msiexec /i 7z.msi /qn",
        [0],
        [3010, 1641],
        detection,
        30,
        false,
        null,
        null,
        new AgentApplicationFiles(new string('a', 64), 1500, 4000, "7z.zip"),
        [Guid.Parse(DependencyId)]);

    [Fact]
    public void NamesTheRouteOfAStepsApplications()
    {
        Assert.Equal(
            $"api/agents/{MachineId}/runs/{RunId}/steps/{StepId}/applications",
            AgentRoutes.RunStepApplications(Guid.Parse(MachineId), Guid.Parse(RunId), Guid.Parse(StepId)));
    }

    [Fact]
    public void WritesAndReadsAnApplication()
    {
        AgentStepApplications applications = new([Application(null)]);

        string json = JsonSerializer.Serialize(applications, AgentJsonContext.Default.AgentStepApplications);

        Assert.Equal(
            $$$"""
            {"applications":[{"id":"{{{ApplicationId}}}","name":"7-Zip","kind":"Command","command":"msiexec /i 7z.msi /qn","successExitCodes":[0],"restartExitCodes":[3010,1641],"detection":null,"timeoutMinutes":30,"continueOnFailure":false,"wingetId":null,"wingetVersion":null,"files":{"sha256":"{{{new string('a', 64)}}}","sizeBytes":1500,"expandedBytes":4000,"name":"7z.zip"},"dependsOn":["{{{DependencyId}}}"]}]}
            """,
            json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentStepApplications)!, AgentJsonContext.Default.AgentStepApplications));
    }

    [Fact]
    public void ReadsEveryKindOfDetection()
    {
        ApplicationDetection[] detections =
        [
            new ProductCodeDetection("{23170F69-40C1-2702-2301-000001000000}"),
            new UninstallKeyDetection("7-Zip"),
            new FileDetection(@"C:\Program Files\7-Zip\7z.exe", "23.1"),
            new PackageFamilyDetection("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "1.21.10120.0"),
        ];

        foreach (ApplicationDetection detection in detections)
        {
            string json = JsonSerializer.Serialize(new AgentStepApplications([Application(detection)]), AgentJsonContext.Default.AgentStepApplications);
            AgentStepApplications? read = JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentStepApplications);

            Assert.Equal(detection, Assert.Single(read?.Applications ?? []).Detection);
        }

        Assert.Contains(
            """
            "detection":{"kind":"file","path":"C:\\Program Files\\7-Zip\\7z.exe","minimumVersion":"23.1"}
            """,
            JsonSerializer.Serialize(new AgentStepApplications([Application(detections[2])]), AgentJsonContext.Default.AgentStepApplications),
            StringComparison.Ordinal);
        Assert.Contains(
            """
            "detection":{"kind":"packageFamily","familyName":"Microsoft.DesktopAppInstaller_8wekyb3d8bbwe","minimumVersion":"1.21.10120.0"}
            """,
            JsonSerializer.Serialize(new AgentStepApplications([Application(detections[3])]), AgentJsonContext.Default.AgentStepApplications),
            StringComparison.Ordinal);
    }

    // A winget application has nothing to download.
    [Fact]
    public void ReadsAnApplicationWithoutFiles()
    {
        AgentStepApplications? read = JsonSerializer.Deserialize(
            $$"""{"applications":[{"id":"{{ApplicationId}}","name":"Terminal","kind":"Winget","command":null,"successExitCodes":[0],"restartExitCodes":[],"detection":null,"timeoutMinutes":60,"continueOnFailure":true,"wingetId":"Microsoft.WindowsTerminal","wingetVersion":null,"files":null,"dependsOn":[]}]}""",
            AgentJsonContext.Default.AgentStepApplications);

        AgentApplication terminal = Assert.Single(read?.Applications ?? []);
        Assert.Equal((ApplicationKind.Winget, "Microsoft.WindowsTerminal", (AgentApplicationFiles?)null, true), (terminal.Kind, terminal.WingetId, terminal.Files, terminal.ContinueOnFailure));
        Assert.Empty(terminal.DependsOn);
    }
}
