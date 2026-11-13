// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The answer file's version 7 settings, on the Deployment defaults page and on the step. Never quotes a product key.

    public static readonly MessageTemplate SettingsDeploymentProductKeyInvalid = Define(
        "settings.deployment.productKeyInvalid",
        "A product key is five groups of five letters or digits: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX.");

    public static readonly MessageTemplate SettingsDeploymentRegisteredTooLong = Define(
        "settings.deployment.registeredTooLong",
        "Use at most {max} characters.");

    public static readonly MessageTemplate SequenceUiLanguage = Define(
        "sequence.uiLanguage",
        "''{value}'' is not a language tag such as de-DE.");

    public static readonly MessageTemplate SequenceRegisteredTooLong = Define(
        "sequence.registeredTooLong",
        "Use at most {max} characters.");
}
