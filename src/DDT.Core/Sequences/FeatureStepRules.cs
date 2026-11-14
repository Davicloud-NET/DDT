// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of an Enable roles and features step. Only DISM on the machine knows whether a name exists.
internal static partial class FeatureStepRules
{
    public static void Check(EnableFeaturesStep step, StepProblems problems)
    {
        IReadOnlyList<string?> features = step.Features ?? [];

        if (features.Count == 0)
        {
            problems.Add("features", ServerMessages.SequenceFeaturesChoose.With());
        }
        else if (features.Count > EnableFeaturesStep.MaxFeatures)
        {
            problems.Add("features", ServerMessages.SequenceFeaturesTooMany.With("max", EnableFeaturesStep.MaxFeatures));
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < features.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"features[{index}]");

            if (features[index] is not { } name || !IsName(name))
            {
                problems.Add(at, ServerMessages.SequenceFeatureName.With("max", EnableFeaturesStep.MaxNameLength));
            }
            else if (!seen.Add(name))
            {
                problems.Add(at, ServerMessages.SequenceFeatureRepeated.With());
            }
        }
    }

    // DISM names features like Microsoft-Hyper-V-All or NetFx3. A space or a quote would break its command line.
    public static bool IsName(string name) => name.Length <= EnableFeaturesStep.MaxNameLength && FeatureName().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureName();
}
