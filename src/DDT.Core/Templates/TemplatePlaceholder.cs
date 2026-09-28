// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Templates;

// One placeholder of a template as it is written: Text is the whole of it, braces included, such as
// {{SerialNumber|alnum|right:12}}, Name the value it stands for, and Filters what is done to the value, in order.
public sealed record TemplatePlaceholder(string Text, string Name, IReadOnlyList<TemplateFilter> Filters);
