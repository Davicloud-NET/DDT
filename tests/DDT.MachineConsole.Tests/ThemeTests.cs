// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace DDT.MachineConsole.Tests;

// Theme/Tokens.axaml against the tokens.json generate.mjs writes it from: fails as soon as either changes without the
// other, or a type names a face the console does not carry.
public sealed class ThemeTests
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace s_avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void HasEveryColourOfTheTokensInBothThemes()
    {
        using JsonDocument tokens = Tokens();
        XElement resources = Resources();
        List<string> problems = [];

        foreach (string theme in new[] { "light", "dark" })
        {
            Dictionary<string, string> written = resources
                .Descendants(s_avalonia + "ResourceDictionary")
                .Single(dictionary => (string?)dictionary.Attribute(s_x + "Key") == Capitalized(theme))
                .Elements(s_avalonia + "SolidColorBrush")
                .ToDictionary(brush => (string)brush.Attribute(s_x + "Key")!, brush => (string)brush.Attribute("Color")!);

            foreach (JsonProperty colour in tokens.RootElement.GetProperty("color").GetProperty(theme).EnumerateObject())
            {
                string key = "Sg" + Pascal(colour.Name);
                string expected = Argb(colour.Value.GetString()!);

                if (!written.TryGetValue(key, out string? value))
                {
                    problems.Add($"{theme}: {key} is missing");
                }
                else if (!string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{theme}: {key} is {value}, the tokens say {expected}");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void HasTheRadiiAndTypesOfTheTokens()
    {
        using JsonDocument tokens = Tokens();
        Dictionary<string, string> written = Resources()
            .Elements()
            .Where(element => element.Attribute(s_x + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(s_x + "Key")!, element => element.Value);

        foreach (JsonProperty radius in tokens.RootElement.GetProperty("radius").EnumerateObject().Where(property => !property.Name.StartsWith('$')))
        {
            Assert.Equal(Pixels(radius.Value.GetString()!), double.Parse(written["SgRadius" + Pascal(radius.Name)], CultureInfo.InvariantCulture));
        }

        foreach (JsonProperty type in tokens.RootElement.GetProperty("type").EnumerateObject().Where(property => !property.Name.StartsWith('$')))
        {
            string key = "SgType" + Pascal(type.Name);
            double size = Pixels(type.Value.GetProperty("size").GetString()!);
            string family = type.Value.TryGetProperty("family", out JsonElement mono) && mono.GetString() == "mono" ? "Martian Mono" : "Archivo";
            string stretch = type.Value.GetProperty("stretch").GetString()!.TrimEnd('%');

            Assert.Equal(size, double.Parse(written[key + "Size"], CultureInfo.InvariantCulture));
            Assert.Equal(
                size * double.Parse(type.Value.GetProperty("lineHeight").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(written[key + "LineHeight"], CultureInfo.InvariantCulture),
                3);
            Assert.Equal($"avares://ddt-console/Assets/Fonts#{family} {type.Value.GetProperty("weight").GetInt32()} {stretch}", written[key + "Family"]);
        }
    }

    [Fact]
    public void HasTheMotionOfTheTokens()
    {
        using JsonDocument tokens = Tokens();
        JsonElement motion = tokens.RootElement.GetProperty("motion");
        Dictionary<string, XElement> written = Resources()
            .Elements()
            .Where(element => element.Attribute(s_x + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(s_x + "Key")!);
        string[] durations = ["press", "fast", "normal", "slow", "flash"];
        string[] easings = ["easing", "enter", "exit"];

        // A motion token the console does not carry yet fails here.
        Assert.Equal(
            [.. durations.Concat(easings).Append("distance").Order(StringComparer.Ordinal)],
            motion.EnumerateObject().Select(property => property.Name).Where(name => !name.StartsWith('$')).Order(StringComparer.Ordinal));

        foreach (string name in durations)
        {
            XElement duration = written["SgMotion" + Capitalized(name)];
            double milliseconds = double.Parse(motion.GetProperty(name).GetString()!.TrimEnd('m', 's'), CultureInfo.InvariantCulture);

            Assert.Equal(s_x + "TimeSpan", duration.Name);
            Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), TimeSpan.Parse(duration.Value, CultureInfo.InvariantCulture));
        }

        foreach (string name in easings)
        {
            XElement easing = written["SgMotion" + Capitalized(name)];
            string bezier = motion.GetProperty(name).GetString()!;
            double[] expected = [.. bezier["cubic-bezier(".Length..^1].Split(',').Select(part => double.Parse(part, CultureInfo.InvariantCulture))];

            Assert.Equal(s_avalonia + "SplineEasing", easing.Name);
            Assert.Equal(expected, new[] { "X1", "Y1", "X2", "Y2" }.Select(point => double.Parse((string)easing.Attribute(point)!, CultureInfo.InvariantCulture)));
        }

        Assert.Equal(Pixels(motion.GetProperty("distance").GetString()!), double.Parse(written["SgMotionDistance"].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LoadsTheMotionAsTheTypesTheConsoleAnimatesWith()
    {
        (object? fast, object? enter, object? distance) = await Headless.RunAsync(() =>
        {
            Avalonia.Application application = Avalonia.Application.Current!;

            return (application.FindResource("SgMotionFast"), application.FindResource("SgMotionEnter"), application.FindResource("SgMotionDistance"));
        });

        Assert.Equal(TimeSpan.FromMilliseconds(120), Assert.IsType<TimeSpan>(fast));
        Assert.Equal(0.2, Assert.IsType<Avalonia.Animation.Easings.SplineEasing>(enter).X2);
        Assert.Equal(6d, Assert.IsType<double>(distance));
    }

    [Fact]
    public async Task CarriesTheFaceOfEveryType()
    {
        List<string> families = [.. Resources().Elements(s_avalonia + "FontFamily").Select(element => element.Value).Distinct()];

        List<string> missing = await Headless.RunAsync(() => families
            .Where(family => !FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family)), out GlyphTypeface? face)
                || face.FamilyName != family.Split('#')[1])
            .ToList());

        Assert.NotEmpty(families);
        Assert.Empty(missing);
    }

    private static JsonDocument Tokens() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root(), "src", "DDT.Design", "tokens.json")));

    private static XElement Resources() => XElement.Load(Path.Combine(Repository.Console, "Theme", "Tokens.axaml"));

    private static string Capitalized(string theme) => char.ToUpperInvariant(theme[0]) + theme[1..];

    private static string Pascal(string name) => string.Concat(name.Split('-').Select(Capitalized));

    private static double Pixels(string value) => double.Parse(value.TrimEnd('p', 'x'), CultureInfo.InvariantCulture);

    // #RRGGBB or rgb(r g b / alpha) as #AARRGGBB.
    private static string Argb(string value)
    {
        if (value.StartsWith('#'))
        {
            return "#FF" + value[1..];
        }

        string[] parts = value["rgb(".Length..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int alpha = (int)Math.Round(double.Parse(parts[4], CultureInfo.InvariantCulture) * 255);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{alpha:X2}{int.Parse(parts[0], CultureInfo.InvariantCulture):X2}{int.Parse(parts[1], CultureInfo.InvariantCulture):X2}{int.Parse(parts[2], CultureInfo.InvariantCulture):X2}");
    }
}
