// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;

namespace DDT.Server.Tests;

// The sequence API as the editor speaks it.
internal static class SequenceRequests
{
    public const string Sequences = "/api/sequences";

    public static SequenceDefinition Definition(params SequenceStep[] steps) => new(SequenceDefinition.CurrentVersion, steps);

    // Records compare their lists by reference, so documents are compared as the server writes them.
    public static string Json(SequenceDefinition definition) => JsonSerializer.Serialize(definition, DdtJsonContext.Default.SequenceDefinition);

    public static string Json(SequenceStep step) => JsonSerializer.Serialize(step, DdtJsonContext.Default.SequenceStep);

    // Runs without problems once the image exists: partition, then apply it.
    public static SequenceDefinition Minimal(Guid imageId) => Definition(
        new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" },
        new ApplyImageStep { Id = Guid.NewGuid(), Name = "Apply", ImageId = imageId });

    // Writes the raw disk image and a seed that names the machine, as the Install Linux template does.
    public static SequenceDefinition Linux(Guid imageId) => Definition(
        new WriteRawImageStep { Id = Guid.NewGuid(), Name = "Write the disk", ImageId = imageId },
        new WriteCloudInitSeedStep
        {
            Id = Guid.NewGuid(),
            Name = "Seed",
            MetaData = Server.Sequences.SequenceTemplates.LinuxMetaData,
            UserData = Server.Sequences.SequenceTemplates.LinuxUserData,
        });

    // Erases nothing and needs no library: one cmd script in Windows PE.
    public static SequenceDefinition ScriptOnly() => Definition(
        new RunScriptStep
        {
            Id = Guid.NewGuid(),
            Name = "Say hello",
            Phase = SequencePhase.WindowsPE,
            Interpreter = ScriptInterpreter.Cmd,
            Script = "echo hello",
            RebootExitCodes = [],
        });

    public static Task<HttpResponseMessage> CreateSequenceAsync(this SignedInClient client, SequenceDefinition definition, string? name = null) =>
        client.PostAsync(Sequences, new CreateSequenceRequest(name ?? $"Sequence {Guid.NewGuid():N}", null, definition));

    public static async Task<SequenceView> CreatedSequenceAsync(this SignedInClient client, SequenceDefinition definition, string? name = null) =>
        await RegisteredMachine.ReadAsync<SequenceView>(await client.CreateSequenceAsync(definition, name));

    public static Task<HttpResponseMessage> SaveSequenceAsync(this SignedInClient client, SequenceView view, SequenceDefinition? definition = null, string? name = null) =>
        client.PutAsync(
            $"{Sequences}/{view.Id}",
            new SaveSequenceRequest(view.Revision, name ?? view.Name, view.Description, definition ?? view.Definition));

    public static async Task<HttpResponseMessage> SendJsonAsync(this SignedInClient client, HttpMethod method, string path, string json)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return await client.SendAsync(request);
    }
}
