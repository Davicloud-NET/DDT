// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Intune's CSV import takes plain ASCII without quotation marks, so a tag can't hold a comma or a quote.
public static class AutopilotGroupTags
{
    public static bool IsValid(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return tag.Length <= RegisterAutopilotStep.MaxGroupTagLength && tag.All(c => c is >= ' ' and <= '~' and not (',' or '"'));
    }
}
