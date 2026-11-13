// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of an Applications input: what it offers, what starts ticked, and none of the other kinds' members.
internal static class ApplicationInputRules
{
    public static void Check(InputDeclaration input, string at, Action<string?, ServerMessage> add)
    {
        if (input.Kind != InputKind.Applications)
        {
            if (input.Offered is { Count: > 0 } || input.Ticked is { Count: > 0 })
            {
                add($"{at}.offered", ServerMessages.SequenceInputOfferedOnlyApplications.With());
            }

            return;
        }

        IReadOnlyList<ApplicationReference?> offered = input.Offered ?? [];

        if (offered.Count is 0 or > SequenceValidator.MaxApplications)
        {
            add($"{at}.offered", ServerMessages.SequenceInputOffered.With("max", SequenceValidator.MaxApplications));
        }

        ApplicationStepRules.CheckList(offered, $"{at}.offered", add);
        CheckTicked(input.Ticked ?? [], offered, at, add);

        if (input.Choices is { Count: > 0 })
        {
            add($"{at}.choices", ServerMessages.SequenceInputNotForApplications.With("field", "choices"));
        }

        if (!string.IsNullOrEmpty(input.Default))
        {
            add($"{at}.default", ServerMessages.SequenceInputNotForApplications.With("field", "default"));
        }

        if (input.MaxLength is not null)
        {
            add($"{at}.maxLength", ServerMessages.SequenceInputNotForApplications.With("field", "maxLength"));
        }
    }

    private static void CheckTicked(IReadOnlyList<Guid> ticked, IReadOnlyList<ApplicationReference?> offered, string at, Action<string?, ServerMessage> add)
    {
        HashSet<Guid> ids = [.. offered.OfType<ApplicationReference>().Select(reference => reference.Id)];

        for (int index = 0; index < ticked.Count; index++)
        {
            if (!ids.Contains(ticked[index]))
            {
                add(string.Create(CultureInfo.InvariantCulture, $"{at}.ticked[{index}]"), ServerMessages.SequenceInputTickedNotOffered.With());
            }
        }
    }
}
