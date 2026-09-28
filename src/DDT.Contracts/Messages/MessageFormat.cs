// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DDT.Contracts.Messages;

// The part of ICU MessageFormat that the catalog and the web's Lingui share: {name}, plural with # and select. Plural
// forms are English only. Apostrophes quote as in ICU: '' is one, and one before {, } or a plural's # quotes up to the next.
public static class MessageFormat
{
    // Throws FormatException for a template this formatter, and so the web, would read differently.
    public static IReadOnlyList<string> Arguments(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        SortedSet<string> names = new(StringComparer.Ordinal);
        Collect(Parse(template), names);

        return [.. names];
    }

    // A value the arguments lack stays as {name}, so a mistake shows in the text rather than failing the request.
    public static string Format(string template, IReadOnlyDictionary<string, object> arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder text = new(template.Length);
        Write(Parse(template), arguments, null, text);

        return text.ToString();
    }

    internal static IReadOnlyList<Node> Parse(string template)
    {
        Parser parser = new(template);
        List<Node> nodes = parser.Message(inPlural: false);

        return parser.AtEnd ? nodes : throw parser.Error("a } that closes nothing");
    }

    internal static void Write(IReadOnlyList<Node> nodes, IReadOnlyDictionary<string, object> arguments, long? count, StringBuilder text)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case TextNode literal:
                    text.Append(literal.Text);
                    break;
                case CountNode:
                    text.Append(count?.ToString(CultureInfo.InvariantCulture) ?? "#");
                    break;
                case ArgumentNode argument when arguments.TryGetValue(argument.Name, out object? value):
                    text.Append(Text(value));
                    break;
                case ArgumentNode argument:
                    text.Append('{').Append(argument.Name).Append('}');
                    break;
                case ChoiceNode choice when choice.Plural:
                    long? number = arguments.TryGetValue(choice.Name, out object? counted) ? Number(counted) : null;
                    Write(PluralForm(choice, number), arguments, number, text);
                    break;
                case ChoiceNode choice:
                    string? key = arguments.TryGetValue(choice.Name, out object? selected) ? Text(selected) : null;
                    Write(SelectForm(choice, key), arguments, count, text);
                    break;
            }
        }
    }

    // An exact form such as =0 first, then English's one for 1 and other for the rest.
    private static IReadOnlyList<Node> PluralForm(ChoiceNode choice, long? number)
    {
        if (number is { } exact && choice.Forms.TryGetValue($"={exact}", out IReadOnlyList<Node>? exactForm))
        {
            return exactForm;
        }

        return choice.Forms.GetValueOrDefault(number == 1 ? "one" : "other") ?? choice.Forms["other"];
    }

    private static IReadOnlyList<Node> SelectForm(ChoiceNode choice, string? key) =>
        key is not null && choice.Forms.TryGetValue(key, out IReadOnlyList<Node>? chosen) ? chosen : choice.Forms["other"];

    // A message received as JSON has its values as JsonElement, a nested message as an object with a code and values.
    private static string Text(object value) => value switch
    {
        string text => text,
        ServerMessage message => message.Text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
        JsonElement { ValueKind: JsonValueKind.Object } element when ServerMessage.FromJson(element) is { } nested => nested.Text,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static long? Number(object value) => value switch
    {
        int number => number,
        long number => number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out long number) => number,
        _ => null,
    };

    private static void Collect(IReadOnlyList<Node> nodes, ISet<string> names)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case ArgumentNode argument:
                    names.Add(argument.Name);
                    break;
                case ChoiceNode choice:
                    names.Add(choice.Name);

                    foreach (IReadOnlyList<Node> form in choice.Forms.Values)
                    {
                        Collect(form, names);
                    }

                    break;
            }
        }
    }

    internal abstract record Node;

    internal sealed record TextNode(string Text) : Node;

    internal sealed record CountNode : Node;

    internal sealed record ArgumentNode(string Name) : Node;

    internal sealed record ChoiceNode(string Name, bool Plural, IReadOnlyDictionary<string, IReadOnlyList<Node>> Forms) : Node;

    private sealed class Parser(string template)
    {
        private int _position;

        public bool AtEnd => _position == template.Length;

        public List<Node> Message(bool inPlural)
        {
            List<Node> nodes = [];
            StringBuilder literal = new();

            void Flush()
            {
                if (literal.Length > 0)
                {
                    nodes.Add(new TextNode(literal.ToString()));
                    literal.Clear();
                }
            }

            while (_position < template.Length)
            {
                char current = template[_position];

                if (current == '}')
                {
                    break;
                }

                if (current == '{')
                {
                    Flush();
                    nodes.Add(Argument());
                }
                else if (current == '#' && inPlural)
                {
                    Flush();
                    nodes.Add(new CountNode());
                    _position++;
                }
                else if (current == '\'')
                {
                    literal.Append(Apostrophe(inPlural));
                }
                else
                {
                    literal.Append(current);
                    _position++;
                }
            }

            Flush();

            return nodes;
        }

        public FormatException Error(string problem) =>
            new($"The message \"{template}\" has {problem} at position {_position.ToString(CultureInfo.InvariantCulture)}.");

        private string Apostrophe(bool inPlural)
        {
            _position++;

            if (Peek() == '\'')
            {
                _position++;

                return "'";
            }

            if (Peek() is not ('{' or '}') && !(inPlural && Peek() == '#'))
            {
                return "'";
            }

            StringBuilder quoted = new();

            while (_position < template.Length)
            {
                if (template[_position] != '\'')
                {
                    quoted.Append(template[_position++]);
                }
                else if (_position + 1 < template.Length && template[_position + 1] == '\'')
                {
                    quoted.Append('\'');
                    _position += 2;
                }
                else
                {
                    _position++;

                    return quoted.ToString();
                }
            }

            throw Error("a quote that does not end");
        }

        private Node Argument()
        {
            _position++;
            string name = Name();

            if (Peek() == '}')
            {
                _position++;

                return new ArgumentNode(name);
            }

            Expect(',');
            string kind = Name();

            if (kind is not ("plural" or "select"))
            {
                throw Error($"the argument type {kind}, which is neither plural nor select");
            }

            Expect(',');
            Dictionary<string, IReadOnlyList<Node>> forms = new(StringComparer.Ordinal);

            while (true)
            {
                SkipSpace();

                if (Peek() == '}')
                {
                    _position++;

                    break;
                }

                string key = Peek() == '=' ? Exact() : Name();
                SkipSpace();
                Expect('{');
                forms[key] = Message(kind == "plural");
                Expect('}');
            }

            return forms.ContainsKey("other")
                ? new ChoiceNode(name, kind == "plural", forms)
                : throw Error($"a {kind} of {name} without other");
        }

        private string Exact()
        {
            int start = _position++;

            while (_position < template.Length && char.IsAsciiDigit(template[_position]))
            {
                _position++;
            }

            return _position > start + 1 ? template[start.._position] : throw Error("= without a number");
        }

        private string Name()
        {
            SkipSpace();
            int start = _position;

            while (_position < template.Length && (char.IsAsciiLetterOrDigit(template[_position]) || template[_position] == '_'))
            {
                _position++;
            }

            if (_position == start || char.IsAsciiDigit(template[start]))
            {
                throw Error("an argument without a name");
            }

            string name = template[start.._position];
            SkipSpace();

            return name;
        }

        private void Expect(char expected)
        {
            SkipSpace();

            if (Peek() != expected)
            {
                throw Error($"no {expected}");
            }

            _position++;
        }

        private void SkipSpace()
        {
            while (_position < template.Length && char.IsWhiteSpace(template[_position]))
            {
                _position++;
            }
        }

        private char? Peek() => _position < template.Length ? template[_position] : null;
    }
}
