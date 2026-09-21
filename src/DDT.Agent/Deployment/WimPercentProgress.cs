// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Wim;

namespace DDT.Agent.Deployment;

// Turns wimlib's byte counts into whole percents, passed on only when the number changes.
internal sealed class WimPercentProgress(IProgress<int> percent) : IProgress<WimProgress>
{
    private int _last = -1;

    public void Report(WimProgress value)
    {
        if (value.TotalBytes <= 0)
        {
            return;
        }

        int current = (int)Math.Clamp(value.CompletedBytes * 100 / value.TotalBytes, 0, 100);

        if (current != _last)
        {
            _last = current;
            percent.Report(current);
        }
    }
}
