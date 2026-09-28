// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Core.Templates;

// One placeholder of a template as it is written: Text is the whole of it, braces included, such as
// {{SerialNumber|alnum|right:12}}, Name the value it stands for, and Filters what is done to the value, in order.
public sealed record TemplatePlaceholder(string Text, string Name, IReadOnlyList<TemplateFilter> Filters);

// A filter as it is written: its name, and what follows its colon, such as 12 in right:12. Null is no colon.
public sealed record TemplateFilter(string Name, string? Argument)
{
    // The number of characters left:n and right:n take, or null when the argument is not a whole number of 1 to
    // ValueTemplate.MaxCount written in the digits 0 to 9.
    public int? Count =>
        Argument is { Length: > 0 and <= 4 } digits
        && digits.All(char.IsAsciiDigit)
        && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
        && count is >= 1 and <= ValueTemplate.MaxCount
            ? count
            : null;
}
