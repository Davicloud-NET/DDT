// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Templates;

// A template that could not be rendered. The message is the problem's English, which names the placeholder.
public sealed class TemplateException : InvalidOperationException
{
    public TemplateException()
    {
    }

    public TemplateException(string message)
        : base(message)
    {
    }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TemplateException(TemplateProblem problem)
        : base((problem ?? throw new ArgumentNullException(nameof(problem))).Message().Text)
    {
        Problem = problem;
    }

    public TemplateProblem? Problem { get; }
}
