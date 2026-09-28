// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Messages;
using DDT.Core.Templates;
using Xunit;

namespace DDT.Server.Tests;

// The web's template field mirrors ValueTemplate by hand, for its completion and its preview, and checks its mirror
// against the cases written here: for each template and values, the names it uses, what Parse reports with the values'
// names as the known ones, and what it renders to or the problem that stops it.
public sealed class TemplateFixtureTests
{
    private static readonly Dictionary<string, string> s_machine = new(StringComparer.Ordinal)
    {
        ["ComputerName"] = "PC-01",
        ["SerialNumber"] = "CN-0K2P1X-12345-ABC-9876",
        ["Office"] = "ProPlus",
        ["Padded"] = "  Wien \t",
        ["Empty"] = "",
        ["Site_2"] = "Graz",
        ["Owner"] = "Grüße-1 à",
    };

    private static readonly string[] s_templates =
    [
        "PC-{{SerialNumber|alnum|right:12}}",
        "{{ComputerName}}",
        "{{ computername | lower }}",
        "{{Office|upper}}",
        "{{Office|lower}}",
        "[{{Padded|trim}}]",
        "{{Office|left:3}}",
        "{{Office|right:4}}",
        "{{Office|left:50}}",
        "{{Office|left:1024}}",
        "{{Office|Left : 3}}",
        "{{Office|upper|left:3|lower}}",
        "{{SerialNumber|right:6|alnum}}",
        "{{Owner|alnum|upper}}",
        "[{{Empty|upper}}]",
        "{{Site_2}}-{{office}}",
        "{{{Office}}}",
        "line\r\nnext {{Office}}\n",
        "{{ v1.local_hostname }} {{ }} {{1x}} {Office} {{Office",
        "OU={{Site}},DC=corp,DC=example",
        "{{Ofice}} and {{Ofice}}",
        "{{Office|capitalise}}",
        "{{Office|}}",
        "{{Office|left}}",
        "{{Office|left:0}}",
        "{{Office|right:1025}}",
        "{{Office|right:x}}",
        "{{Office|upper:2}}",
        "{{Missing}}-{{Office|bad}}",
        "{{Missing|bad}}",
    ];

    [Fact]
    public async Task TheWebFixtureHoldsTheTemplateCases()
    {
        JsonArray cases = new();

        foreach (string template in s_templates)
        {
            ParsedTemplate parsed = ValueTemplate.Parse(template, name => s_machine.Keys.Contains(name, StringComparer.OrdinalIgnoreCase));
            bool rendered = ValueTemplate.TryRender(template, ValueTemplate.Lookup(s_machine), out string output, out TemplateProblem? problem);
            JsonObject values = new();

            foreach (string name in parsed.Names)
            {
                if (ValueTemplate.Lookup(s_machine)(name) is { } value)
                {
                    values[s_machine.Keys.First(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))] = value;
                }
            }

            cases.Add(new JsonObject
            {
                ["template"] = template,
                ["values"] = values,
                ["names"] = new JsonArray([.. parsed.Names.Select(name => (JsonNode)name)]),
                ["problems"] = new JsonArray([.. parsed.Problems.Select(Problem)]),
                ["output"] = rendered ? output : null,
                ["error"] = problem is null ? null : Problem(problem),
            });
        }

        JsonObject fixture = new()
        {
            ["filters"] = new JsonArray([.. ValueTemplate.Filters.Select(filter => (JsonNode)filter)]),
            ["maxCount"] = ValueTemplate.MaxCount,
            ["cases"] = cases,
        };

        await MatchFixtureAsync("template-cases.json", fixture);
    }

    private static JsonNode Problem(TemplateProblem problem)
    {
        ServerMessage message = problem.Message();
        JsonObject args = new();

        foreach ((string name, object value) in message.Args.OrderBy(arg => arg.Key, StringComparer.Ordinal))
        {
            args[name] = value switch
            {
                int number => number,
                long number => number,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }

        return new JsonObject
        {
            ["kind"] = JsonNamingPolicy.CamelCase.ConvertName(problem.Kind.ToString()),
            ["placeholder"] = problem.Placeholder,
            ["name"] = problem.Name,
            ["filter"] = problem.Filter,
            ["code"] = message.Code,
            ["args"] = args,
            ["message"] = message.Text,
        };
    }

    private static async Task MatchFixtureAsync(string name, JsonNode value)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Repository.Root(), "src", "DDT.Web", "src", "test", "fixtures");
        string path = Path.Combine(folder, name);

        // Indented with LF line ends; the web's .prettierignore leaves the fixtures as written here.
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string expected = value.ToJsonString(options) + "\n";
        string? actual = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        if (actual == expected)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, expected, cancellationToken);

        Assert.Fail($"{path} did not match the cases built here and was written again. Check the web's template mirror against it, then commit it.");
    }
}
