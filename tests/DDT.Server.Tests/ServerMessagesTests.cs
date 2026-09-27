// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDT.Contracts;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace DDT.Server.Tests;

public sealed partial class ServerMessagesTests
{
    private static readonly Dictionary<string, object> s_none = [];

    private static Dictionary<string, object> Values(string name, object value) => new(StringComparer.Ordinal) { [name] = value };

    [GeneratedRegex("^[a-z][A-Za-z0-9]*(\\.[a-z][A-Za-z0-9]*)+$")]
    private static partial Regex CodePattern();

    [Fact]
    public void EveryCodeIsDottedAndListedOnce()
    {
        Assert.NotEmpty(ServerMessages.All);
        Assert.All(ServerMessages.All, template => Assert.Matches(CodePattern(), template.Code));
        Assert.Equal(ServerMessages.All.Count, ServerMessages.All.DistinctBy(template => template.Code, StringComparer.Ordinal).Count());
        Assert.All(ServerMessages.All, template => Assert.Same(template, ServerMessages.Find(template.Code)));
        Assert.Null(ServerMessages.Find("machine.noSuchCode"));
    }

    // A message defined outside the list would never reach the web's catalog.
    [Fact]
    public void EveryMessageFieldIsInTheList()
    {
        MessageTemplate[] fields =
        [
            .. typeof(ServerMessages)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(MessageTemplate))
                .Select(field => (MessageTemplate)field.GetValue(null)!),
        ];

        Assert.Equal(ServerMessages.All.Count, fields.Length);
        Assert.All(fields, field => Assert.Contains(field, ServerMessages.All));
    }

    // Every template has an English text, and one with values can be said with them.
    [Fact]
    public void EveryCodeHasATemplateThatFormats()
    {
        foreach (MessageTemplate template in ServerMessages.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(template.English), template.Code);

            Dictionary<string, object> values = template.Arguments.ToDictionary(name => name, name => (object)1, StringComparer.Ordinal);
            string text = MessageFormat.Format(template.English, values);

            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("''", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormatsNamedValues()
    {
        Assert.Equal("The machine is Rejected.", ServerMessages.MachineInState.With("state", "Rejected").Text);
        Assert.Equal(
            "A sequence needs 1 to 100 steps.",
            MessageFormat.Format("A sequence needs 1 to {max} steps.", Values("max", 100)));
    }

    [Fact]
    public void LeavesAMissingValueAsItsName() =>
        Assert.Equal("Step {number} has no id.", MessageFormat.Format("Step {number} has no id.", s_none));

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "1 rule")]
    [InlineData(2, "2 rules")]
    [InlineData(1234, "1234 rules")]
    public void ChoosesThePluralForm(int count, string expected) =>
        Assert.Equal(
            expected,
            MessageFormat.Format("{count, plural, =0 {none} one {# rule} other {# rules}}", Values("count", count)));

    [Fact]
    public void ChoosesBySelectAndFallsBackToOther()
    {
        const string Template = "The sequence cannot skip {activity, select, partition {partitioning the disk} other {writing}}.";

        Assert.Equal("The sequence cannot skip partitioning the disk.", MessageFormat.Format(Template, Values("activity", "partition")));
        Assert.Equal("The sequence cannot skip writing.", MessageFormat.Format(Template, Values("activity", "rawImage")));
    }

    // As ICU and Lingui read them: '' is one apostrophe, one before a brace quotes, any other stays.
    [Fact]
    public void ReadsApostrophesAsIcuDoes()
    {
        Dictionary<string, object> values = Values("value", "OU=x");

        Assert.Equal("'OU=x' is not a distinguished name.", MessageFormat.Format("''{value}'' is not a distinguished name.", values));
        Assert.Equal("The sequence's page", MessageFormat.Format("The sequence's page", s_none));
        Assert.Equal("Write {value} as is", MessageFormat.Format("Write '{value}' as is", values));
        Assert.Equal("Digits ('0'-'9')", MessageFormat.Format("Digits ('0'-'9')", s_none));
    }

    [Fact]
    public void SaysANestedMessageInItsText()
    {
        ServerMessage rule = ServerMessages.RuleForModelOfAnyMaker.With("model", "Latitude 7*");
        ServerMessage explanation = ServerMessages.ResolutionRuleChooses.With("rule", rule, "sequence", "Lab");

        Assert.StartsWith("The rule for model Latitude 7* of any maker chooses Lab. A rule only chooses:", explanation.Text, StringComparison.Ordinal);
        Assert.Same(rule, explanation.Args["rule"]);
    }

    // A message read back from JSON says the same, its values and nested messages being JsonElement.
    [Fact]
    public void SaysAMessageReadFromJson()
    {
        ServerMessage sent = ServerMessages.DeploymentRulesNoLongerChoose.With(
            "explanation",
            ServerMessages.ResolutionChosenAtMachine.With("by", ServerMessages.SomeOperator.With(), "sequence", "Lab"));
        string json = JsonSerializer.Serialize(sent, DdtJsonContext.Default.ServerMessage);
        ServerMessage received = JsonSerializer.Deserialize(json, DdtJsonContext.Default.ServerMessage)!;

        Assert.Equal(
            """{"code":"deployment.rulesNoLongerChoose","args":{"explanation":{"code":"resolution.chosenAtMachine","args":""" +
            """{"by":{"code":"person.someOperator","args":{}},"sequence":"Lab"}}}}""",
            json);
        Assert.Equal(sent.Text, received.Text);
        Assert.Equal(
            "The rules no longer choose that sequence for this machine. An operator chose Lab at the machine, which comes before every rule. " +
            "Look at the machine again.",
            received.Text);
    }

    [Fact]
    public void AnUnknownCodeSaysItsCode() =>
        Assert.Equal("machine.fromTheFuture", new ServerMessage("machine.fromTheFuture", s_none).Text);

    // A debug build, which the tests run, refuses values the text does not name.
    [Fact]
    public void RefusesValuesTheTextDoesNotName()
    {
        Assert.Throws<ArgumentException>(() => ServerMessages.MachineInState.With());
        Assert.Throws<ArgumentException>(() => ServerMessages.MachineInState.With("status", "Rejected"));
        Assert.Throws<ArgumentException>(() => ServerMessages.MachineNobodySignedIn.With("state", "Rejected"));
    }

    [Theory]
    [InlineData("Step {number has no id.")]
    [InlineData("A } alone")]
    [InlineData("{count, plural, one {# step}}")]
    [InlineData("{count, number}")]
    [InlineData("Quoted '{ without an end")]
    public void RefusesATemplateTheWebWouldReadDifferently(string template) =>
        Assert.Throws<FormatException>(() => MessageFormat.Arguments(template));

    // The describer says Identity's own English, so only the codes are new.
    [Fact]
    public void TheIdentityDescriberSaysWhatIdentitySays()
    {
        IdentityErrorDescriber identity = new();
        DdtIdentityErrorDescriber ddt = new();
        MethodInfo[] methods =
        [
            .. typeof(IdentityErrorDescriber)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.IsVirtual && method.ReturnType == typeof(IdentityError)),
        ];

        Assert.NotEmpty(methods);

        foreach (MethodInfo method in methods)
        {
            object[] values = [.. method.GetParameters().Select(parameter => parameter.ParameterType == typeof(int) ? (object)12 : "jane")];
            IdentityError expected = (IdentityError)method.Invoke(identity, values)!;
            IdentityError actual = (IdentityError)method.Invoke(ddt, values)!;

            Type[] parameters = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];

            Assert.Equal(typeof(DdtIdentityErrorDescriber), typeof(DdtIdentityErrorDescriber).GetMethod(method.Name, parameters)!.DeclaringType);
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.Description, actual.Description);
            Assert.NotNull(DdtIdentityErrorDescriber.MessageOf(actual));
        }
    }

    // src/DDT.Web/scripts/server-messages.json is what `npm run messages` writes the web's catalog from, and a web test
    // fails while the catalog differs from it. Like the fixtures, this writes the file again when it differs, and fails.
    [Fact]
    public async Task TheWebCatalogIsCurrent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = Path.Combine(Repository.Root(), "src", "DDT.Web", "scripts", "server-messages.json");
        SortedDictionary<string, string> catalog = new(
            ServerMessages.All.ToDictionary(template => template.Code, template => template.English, StringComparer.Ordinal),
            StringComparer.Ordinal);
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string expected = JsonSerializer.Serialize(catalog, options) + "\n";
        string? actual = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        if (actual == expected)
        {
            return;
        }

        await File.WriteAllTextAsync(path, expected, cancellationToken);

        Assert.Fail($"{path} did not match ServerMessages and was written again. Run npm run messages and npm run i18n in src/DDT.Web, " +
            "translate the new German messages, then commit them.");
    }
}
