// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Templates;

// What is wrong with a placeholder: Placeholder is its text, braces included, Name the value it names, and Filter the
// filter the problem is about, where it is about one.
public sealed record TemplateProblem(TemplateProblemKind Kind, string Placeholder, string Name, string? Filter = null)
{
    public ServerMessage Message() => Kind switch
    {
        TemplateProblemKind.UnknownName => ServerMessages.ValueTemplateUnknownName.With("name", Name, "placeholder", Placeholder),
        TemplateProblemKind.UnknownFilter => ServerMessages.ValueTemplateUnknownFilter.With(
            "filter",
            Filter ?? "",
            "filters",
            ValueTemplate.FilterList,
            "placeholder",
            Placeholder),
        TemplateProblemKind.FilterNeedsCount => ServerMessages.ValueTemplateFilterNeedsCount.With(
            "filter",
            Filter ?? "",
            "max",
            ValueTemplate.MaxCount,
            "placeholder",
            Placeholder),
        TemplateProblemKind.FilterTakesNoCount =>
            ServerMessages.ValueTemplateFilterTakesNoCount.With("filter", Filter ?? "", "placeholder", Placeholder),
        _ => ServerMessages.ValueTemplateNoValue.With("placeholder", Placeholder),
    };
}

// Parse reports the first four: a name the caller does not know, and filters that are not written as DDT's filters
// are. Rendering reports a filter problem too, and a name without a value.
public enum TemplateProblemKind
{
    UnknownName,
    UnknownFilter,
    FilterNeedsCount,
    FilterTakesNoCount,
    MissingValue,
}
