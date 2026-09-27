// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DDT.Contracts.Messages;

// One message of the catalog: its stable code and its English text as an ICU message, see MessageFormat. With gives the
// message its values by the names the text uses. A debug build refuses values that do not match those names, so a
// mistake fails a test rather than showing {name} to a person.
public sealed class MessageTemplate
{
    private readonly IReadOnlyList<MessageFormat.Node> _nodes;

    internal MessageTemplate(string code, string english)
    {
        Code = code;
        English = english;
        _nodes = MessageFormat.Parse(english);
        Arguments = MessageFormat.Arguments(english);
    }

    public string Code { get; }

    public string English { get; }

    // The names the English text uses, in ordinal order.
    public IReadOnlyList<string> Arguments { get; }

    public ServerMessage With() => Create([]);

    public ServerMessage With(string name, object value) => Create([(name, value)]);

    public ServerMessage With(string name1, object value1, string name2, object value2) =>
        Create([(name1, value1), (name2, value2)]);

    public ServerMessage With(string name1, object value1, string name2, object value2, string name3, object value3) =>
        Create([(name1, value1), (name2, value2), (name3, value3)]);

    public ServerMessage With(
        string name1,
        object value1,
        string name2,
        object value2,
        string name3,
        object value3,
        string name4,
        object value4) =>
        Create([(name1, value1), (name2, value2), (name3, value3), (name4, value4)]);

    public ServerMessage With(
        string name1,
        object value1,
        string name2,
        object value2,
        string name3,
        object value3,
        string name4,
        object value4,
        string name5,
        object value5) =>
        Create([(name1, value1), (name2, value2), (name3, value3), (name4, value4), (name5, value5)]);

    internal string Format(IReadOnlyDictionary<string, object> arguments)
    {
        StringBuilder text = new(English.Length);
        MessageFormat.Write(_nodes, arguments, null, text);

        return text.ToString();
    }

    // An enum goes as its name, which a translation can choose by with select; every other value that is not a string,
    // a whole number or a message goes as its invariant text.
    private ServerMessage Create(ReadOnlySpan<(string Name, object Value)> values)
    {
        Dictionary<string, object> arguments = new(values.Length, StringComparer.Ordinal);

        foreach ((string name, object value) in values)
        {
            arguments[name] = value switch
            {
                string or int or long or ServerMessage => value,
                Enum choice => choice.ToString(),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                null => "",
                _ => value.ToString() ?? "",
            };
        }

        Check(arguments);

        return new ServerMessage(Code, arguments);
    }

    [Conditional("DEBUG")]
    private void Check(Dictionary<string, object> arguments)
    {
        string[] given = [.. arguments.Keys.Order(StringComparer.Ordinal)];

        if (!given.SequenceEqual(Arguments, StringComparer.Ordinal))
        {
            throw new ArgumentException($"{Code} names {string.Join(", ", Arguments)}, but was given {string.Join(", ", given)}.");
        }
    }
}
