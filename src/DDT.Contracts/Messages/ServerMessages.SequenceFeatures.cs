// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Enable roles and features step.

    public static readonly MessageTemplate SequenceFeaturesChoose = Define(
        "sequence.featuresChoose",
        "Add a feature.");

    public static readonly MessageTemplate SequenceFeaturesTooMany = Define(
        "sequence.featuresTooMany",
        "A step enables at most {max} features.");

    public static readonly MessageTemplate SequenceFeatureName = Define(
        "sequence.featureName",
        "A feature name has up to {max} letters, digits, dots, dashes and underscores, as DISM lists it.");

    public static readonly MessageTemplate SequenceFeatureRepeated = Define(
        "sequence.featureRepeated",
        "This feature is listed twice.");
}
