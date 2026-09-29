// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Templates;

// A template's placeholders in written order, repeats included, and its problems, each listed once.
public sealed record ParsedTemplate(IReadOnlyList<TemplatePlaceholder> Placeholders, IReadOnlyList<TemplateProblem> Problems)
{
    // Every name the template uses, once, ignoring case, in the order they first appear.
    public IReadOnlyList<string> Names =>
        [.. Placeholders.Select(placeholder => placeholder.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
}
