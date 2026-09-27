// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Media;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console's look comes from src/DDT.Design/tokens.json, as the web's does: Theme/Tokens.axaml is written from it by
// generate.mjs, and fails here as soon as either changes without the other. The web's own test compares the whole file;
// this one reads both and compares the values, from the .NET side, and checks that every face a type names is one the
// console carries.
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
