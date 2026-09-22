// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// The web client mirrors the sequence contracts by hand and checks its mirror against these files, so a renamed
// field, kind or enum value fails a web test instead of an editor.
public sealed class SequenceFixtureTests
{
    private static readonly Guid s_imageId = new("0193a4b2-0000-7000-8000-0000000000a1");
    private static readonly Guid s_packageId = new("0193a4b2-0000-7000-8000-0000000000b1");

    [Fact]
    public async Task TheWebFixtureIsTheInstallWindowsTemplate()
    {
        SequenceTemplate template = Assert.Single(SequenceTemplates.All(domainConfigured: true, administratorConfigured: true, s_imageId));
        SequenceTemplate fixedIds = template with
        {
            Definition = template.Definition with
            {
                Steps = [.. template.Definition.Steps.Select((step, index) => step with { Id = StepId(index) })],
            },
        };

        await MatchFixtureAsync("install-windows.sequence.json", fixedIds, "the template");
    }

    // Every kind of step, and every phase, interpreter and condition operator.
    [Fact]
    public async Task TheWebFixtureHoldsEveryKindOfStep()
    {
        List<SequenceStep> steps =
        [
            new PartitionStep { Id = StepId(0), Name = "Partition", SystemPartitionMegabytes = 260, RecoveryPartitionMegabytes = 2048 },
            new ApplyImageStep { Id = StepId(1), Name = "Apply", ImageId = s_imageId },
            new InjectDriversStep { Id = StepId(2), Name = "Drivers", RequireMatch = true },
            new WriteUnattendStep
            {
                Id = StepId(3),
                Name = "Answer file",
                TimeZone = "W. Europe Standard Time",
                Locale = "de-AT",
                Keyboard = "0c07:00000407",
                LocalAdministrator = true,
            },
            new JoinDomainStep { Id = StepId(4), Name = "Join", OrganizationalUnit = "OU=Lab,DC=example,DC=org" },
            new RebootStep
            {
                Id = StepId(5),
                Name = "Restart",
                ContinueOnError = true,
                RebootAfter = true,
                Conditions = [.. Enum.GetValues<ConditionOperator>().Select(op => new StepCondition(MachineVariableNames.Model, op, "Latitude 7440"))],
            },
        ];

        foreach (SequencePhase phase in Enum.GetValues<SequencePhase>())
        {
            foreach (ScriptInterpreter interpreter in Enum.GetValues<ScriptInterpreter>())
            {
                steps.Add(new RunScriptStep
                {
                    Id = StepId(steps.Count),
                    Name = $"Script {phase} {interpreter}",
                    Phase = phase,
                    Interpreter = interpreter,
                    Script = "exit 0",
                    PackageId = s_packageId,
                    TimeoutMinutes = 30,
                    SuccessExitCodes = [0, 1],
                    RebootExitCodes = [3010, 1641],
                });
            }
        }

        // A kind added later must be added here too.
        string[] kinds = [.. typeof(SequenceStep).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(kind => kind.DerivedType.Name).Order(StringComparer.Ordinal)];
        string[] built = [.. steps.Select(step => step.GetType().Name).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(kinds, built);

        await MatchFixtureAsync("every-step.sequence.json", new SequenceDefinition(SequenceDefinition.CurrentVersion, steps), "the steps built here");
    }

    private static async Task MatchFixtureAsync<T>(string name, T value, string source)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Repository.Root(), "src", "DDT.Web", "src", "test", "fixtures");
        string path = Path.Combine(folder, name);

        // Indented with LF line ends; the web's .prettierignore leaves the fixtures as written here.
        JsonSerializerOptions options = new(DdtJsonContext.Default.Options)
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string expected = JsonSerializer.Serialize(value, options) + "\n";
        string? actual = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        if (actual == expected)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, expected, cancellationToken);

        Assert.Fail($"{path} did not match {source} and was written again. Check the mirror in src/DDT.Web/src/sequences/sequences.ts against it, then commit it.");
    }

    private static Guid StepId(int index) =>
        new(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{index + 1:D12}"));
}
