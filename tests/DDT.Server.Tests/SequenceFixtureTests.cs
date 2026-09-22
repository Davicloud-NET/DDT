// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// The web client mirrors the sequence contracts by hand and checks its mirror against this file, so a renamed
// field or kind fails a web test instead of an editor. The template with a domain has the most kinds.
public sealed class SequenceFixtureTests
{
    private static readonly Guid s_imageId = new("0193a4b2-0000-7000-8000-0000000000a1");

    [Fact]
    public async Task TheWebFixtureIsTheInstallWindowsTemplate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Repository.Root(), "src", "DDT.Web", "src", "test", "fixtures");
        string path = Path.Combine(folder, "install-windows.sequence.json");

        SequenceTemplate template = Assert.Single(SequenceTemplates.All(domainConfigured: true, administratorConfigured: true, s_imageId));
        SequenceTemplate fixedIds = template with
        {
            Definition = template.Definition with
            {
                Steps = [.. template.Definition.Steps.Select((step, index) => step with { Id = StepId(index) })],
            },
        };

        // Written as Prettier formats JSON, so the web checks leave it as it is.
        JsonSerializerOptions options = new(DdtJsonContext.Default.Options)
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string expected = JsonSerializer.Serialize(fixedIds, options) + "\n";
        string? actual = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        if (actual == expected)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, expected, cancellationToken);

        Assert.Fail($"{path} did not match the template and was written again. Check the mirror in src/DDT.Web/src/sequences/sequences.ts against it, then commit it.");
    }

    private static Guid StepId(int index) =>
        new(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{index + 1:D12}"));
}
