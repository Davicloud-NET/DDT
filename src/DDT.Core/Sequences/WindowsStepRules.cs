// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The kind rules of the steps that service Windows or run in it, from version 6 on.
internal static class WindowsStepRules
{
    // False for a kind that isn't one of them.
    public static bool Check(SequenceStep step, StepPlace place, SequenceNames names, StepProblems problems)
    {
        switch (step)
        {
            case InstallApplicationsStep install:
                ApplicationStepRules.Check(install, names, problems);
                return true;
            case EnableFeaturesStep features:
                FeatureStepRules.Check(features, problems);
                return true;
            case WindowsUpdateStep updates:
                UpdateStepRules.Check(updates, problems);
                return true;
            case ServicePackagesStep servicing:
                ServicingStepRules.Check(servicing, place, problems);
                return true;
            case RegisterAutopilotStep autopilot:
                AutopilotStepRules.Check(autopilot, problems);
                return true;
            case PreprovisionBitLockerStep preprovision:
                BitLockerStepRules.Check(preprovision, place, problems);
                return true;
            case EnableBitLockerStep enable:
                BitLockerStepRules.Check(enable, place, problems);
                return true;
            default:
                return false;
        }
    }
}
