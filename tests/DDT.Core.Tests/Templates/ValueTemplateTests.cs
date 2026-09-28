// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.CloudInit;
using DDT.Core.Templates;
using Xunit;

namespace DDT.Core.Tests.Templates;

public sealed class ValueTemplateTests
{
    private static readonly Dictionary<string, string> s_values = new(StringComparer.Ordinal)
    {
        ["SerialNumber"] = " ab-12 34/5678 ",
        ["Office"] = "ProPlus",
        ["Site"] = "Wien",
        ["Empty"] = "",
    };

    [Theory]
    [InlineData("{{Office|upper}}", "PROPLUS")]
    [InlineData("{{Office|lower}}", "proplus")]
    [InlineData("[{{SerialNumber|trim}}]", "[ab-12 34/5678]")]
    [InlineData("{{SerialNumber|alnum}}", "ab12345678")]
    [InlineData("{{Office|left:3}}", "Pro")]
    [InlineData("{{Office|right:4}}", "Plus")]
    [InlineData("{{Office|left:50}}", "ProPlus")]
    [InlineData("{{Office|right:7}}", "ProPlus")]
    [InlineData("PC-{{SerialNumber|alnum|right:6|upper}}", "PC-345678")]
    [InlineData("{{SerialNumber|right:6|alnum}}", "5678")]
    [InlineData("{{Office|upper|left:3|lower}}", "pro")]
    [InlineData("[{{Empty|upper}}]", "[]")]
    public void DoesEveryFilterInOrder(string template, string rendered)
    {
        Assert.Equal(rendered, ValueTemplate.Render(template, s_values));
    }

    [Theory]
    [InlineData("{{office}}", "ProPlus")]
    [InlineData("{{ OFFICE }}", "ProPlus")]
    [InlineData("{{Office | UPPER | lower}}", "proplus")]
    [InlineData("{{Office|Left : 3}}", "Pro")]
    public void IgnoresCaseAndSpaces(string template, string rendered)
    {
        Assert.Equal(rendered, ValueTemplate.Render(template, s_values));
    }

    [Theory]
    [InlineData("hostname: {{ v1.local_hostname }}")]
    [InlineData("{{ }} and {{1x}} and {Office} and {{Office")]
    [InlineData("no placeholders at all")]
    public void LeavesWhatIsNotAPlaceholder(string template)
    {
        Assert.Equal(template, ValueTemplate.Render(template, s_values));
        Assert.Empty(ValueTemplate.Parse(template).Placeholders);
    }

    [Fact]
    public void NamesAValueThatIsMissing()
    {
        bool rendered = ValueTemplate.TryRender("OU={{Site}},OU={{Region|upper}}", ValueTemplate.Lookup(s_values), out string text, out TemplateProblem? problem);

        Assert.False(rendered);
        Assert.Equal("", text);
        Assert.Equal(new TemplateProblem(TemplateProblemKind.MissingValue, "{{Region|upper}}", "Region"), problem);

        TemplateException refusal = Assert.Throws<TemplateException>(() => ValueTemplate.Render("{{ Region }}", s_values));

        Assert.Equal("The machine has no value for {{ Region }}.", refusal.Message);
        Assert.Equal(TemplateProblemKind.MissingValue, refusal.Problem!.Kind);
        Assert.Equal(ServerMessages.ValueTemplateNoValue.Code, refusal.Problem.Message().Code);
    }

    [Fact]
    public void RendersWithAnyLookup()
    {
        Assert.Equal("x-y", ValueTemplate.Render("{{x}}-{{Y}}", name => name.ToLowerInvariant()));
        Assert.True(ValueTemplate.TryRender("plain", _ => null, out string text, out TemplateProblem? problem));
        Assert.Equal("plain", text);
        Assert.Null(problem);
    }

    [Fact]
    public void RefusesToRenderAFilterWrittenWrong()
    {
        Assert.False(ValueTemplate.TryRender("{{Office|capitalise}}", ValueTemplate.Lookup(s_values), out _, out TemplateProblem? problem));
        Assert.Equal(new TemplateProblem(TemplateProblemKind.UnknownFilter, "{{Office|capitalise}}", "Office", "capitalise"), problem);
        Assert.Throws<ArgumentException>(() => ValueTemplate.Apply(ValueTemplate.Parse("{{Office|left}}").Placeholders[0], "x"));
    }

    [Fact]
    public void ReportsUnknownNamesAndFiltersWrittenWrong()
    {
        ParsedTemplate parsed = ValueTemplate.Parse(
            "{{Office|upper:2}}-{{Ofice}}-{{Site|left}}-{{Site|left:0}}-{{Site|right:x}}-{{Site|right:1025}}-{{Site|camel}}-{{Site|}}-{{ofice}}-{{Ofice}}",
            name => s_values.ContainsKey(name));

        Assert.Equal(
            [
                new TemplateProblem(TemplateProblemKind.FilterTakesNoCount, "{{Office|upper:2}}", "Office", "upper"),
                new TemplateProblem(TemplateProblemKind.UnknownName, "{{Ofice}}", "Ofice"),
                new TemplateProblem(TemplateProblemKind.FilterNeedsCount, "{{Site|left}}", "Site", "left"),
                new TemplateProblem(TemplateProblemKind.FilterNeedsCount, "{{Site|left:0}}", "Site", "left"),
                new TemplateProblem(TemplateProblemKind.FilterNeedsCount, "{{Site|right:x}}", "Site", "right"),
                new TemplateProblem(TemplateProblemKind.FilterNeedsCount, "{{Site|right:1025}}", "Site", "right"),
                new TemplateProblem(TemplateProblemKind.UnknownFilter, "{{Site|camel}}", "Site", "camel"),
                new TemplateProblem(TemplateProblemKind.UnknownFilter, "{{Site|}}", "Site", ""),
                new TemplateProblem(TemplateProblemKind.UnknownName, "{{ofice}}", "ofice"),
            ],
            parsed.Problems);
        Assert.Equal(10, parsed.Placeholders.Count);
        Assert.Equal(["Office", "Ofice", "Site"], parsed.Names);
    }

    [Fact]
    public void KnowsEveryNameWithoutACaller()
    {
        ParsedTemplate parsed = ValueTemplate.Parse("PC-{{SerialNumber|alnum|right:12}}");

        Assert.Empty(parsed.Problems);
        TemplatePlaceholder placeholder = Assert.Single(parsed.Placeholders);
        Assert.Equal("{{SerialNumber|alnum|right:12}}", placeholder.Text);
        Assert.Equal("SerialNumber", placeholder.Name);
        Assert.Equal([new TemplateFilter("alnum", null), new TemplateFilter("right", "12")], placeholder.Filters);
        Assert.Equal(12, placeholder.Filters[1].Count);
        Assert.Null(new TemplateFilter("right", "1025").Count);
        Assert.Equal(ValueTemplate.MaxCount, new TemplateFilter("right", "1024").Count);
    }

    [Fact]
    public void SaysEachProblemAsAServerMessage()
    {
        Assert.Equal(
            "{{Ofice}} uses Ofice, which is not a machine fact or a declared value. Check the spelling.",
            new TemplateProblem(TemplateProblemKind.UnknownName, "{{Ofice}}", "Ofice").Message().Text);
        Assert.Equal(
            "{{Site|camel}} uses the filter 'camel', which DDT does not have. The filters are upper, lower, trim, alnum, left:n, right:n.",
            new TemplateProblem(TemplateProblemKind.UnknownFilter, "{{Site|camel}}", "Site", "camel").Message().Text);
        Assert.Equal(
            "In {{Site|left}}, left needs a number of characters from 1 to 1024, such as left:12.",
            new TemplateProblem(TemplateProblemKind.FilterNeedsCount, "{{Site|left}}", "Site", "left").Message().Text);
        Assert.Equal(
            "In {{Site|upper:2}}, upper takes no number. Remove the colon and what follows it.",
            new TemplateProblem(TemplateProblemKind.FilterTakesNoCount, "{{Site|upper:2}}", "Site", "upper").Message().Text);
        Assert.Equal(
            "The machine has no value for {{Region}}.",
            new TemplateProblem(TemplateProblemKind.MissingValue, "{{Region}}", "Region").Message().Text);
    }

    // The seed takes DDT's filters on its own names, and leaves anything it does not understand for cloud-init.
    [Fact]
    public void LetsTheCloudInitSeedUseFilters()
    {
        Dictionary<string, string?> values = new() { ["ComputerName"] = "PC-01", ["Model"] = "Latitude \"5440\"" };

        Assert.Equal(
            "hostname: \"pc-01\"\nmodel: \"LATITUDE \\\"5440\\\"\"\nkeep: {{ComputerName|capitalise}} {{ hostname | lower }}",
            CloudInitTemplate.Render("hostname: \"{{ComputerName|lower}}\"\r\nmodel: \"{{ model | upper }}\"\nkeep: {{ComputerName|capitalise}} {{ hostname | lower }}", values));
        Assert.Equal(["ComputerName", "Model", "hostname"], CloudInitTemplate.Placeholders("{{ComputerName|lower}} {{Model}} {{ hostname | lower }}"));
    }
}
