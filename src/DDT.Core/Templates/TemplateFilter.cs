// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Core.Templates;

// A filter as written. Argument is what follows its colon, such as 12 in right:12, or null when there's no colon.
public sealed record TemplateFilter(string Name, string? Argument)
{
    // The number of characters left:n and right:n keep. Null when the argument isn't a whole number from 1 to
    // ValueTemplate.MaxCount, written with the digits 0 to 9.
    public int? Count =>
        Argument is { Length: > 0 and <= 4 } digits
        && digits.All(char.IsAsciiDigit)
        && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
        && count is >= 1 and <= ValueTemplate.MaxCount
            ? count
            : null;
}
