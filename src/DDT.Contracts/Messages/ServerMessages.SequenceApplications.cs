// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Install applications step and the Applications input. In the messages, application is an application's name.

    public static readonly MessageTemplate SequenceApplicationsChoose = Define(
        "sequence.applicationsChoose",
        "Choose applications or bundles, or a value that names them.");

    public static readonly MessageTemplate SequenceApplicationsTooMany = Define(
        "sequence.applicationsTooMany",
        "A step installs at most {max} applications and bundles.");

    public static readonly MessageTemplate SequenceApplicationEmpty = Define(
        "sequence.applicationEmpty",
        "Choose an application or a bundle.");

    public static readonly MessageTemplate SequenceApplicationRepeated = Define(
        "sequence.applicationRepeated",
        "This application or bundle is listed twice.");

    public static readonly MessageTemplate SequenceInputOffered = Define(
        "sequence.inputOffered",
        "Offer 1 to {max} applications and bundles.");

    public static readonly MessageTemplate SequenceInputTickedNotOffered = Define(
        "sequence.inputTickedNotOffered",
        "Tick only applications and bundles the input offers.");

    public static readonly MessageTemplate SequenceInputOfferedOnlyApplications = Define(
        "sequence.inputOfferedOnlyApplications",
        "Only an Applications input offers applications.");

    public static readonly MessageTemplate SequenceInputNotForApplications = Define(
        "sequence.inputNotForApplications",
        "An Applications input {field, select, choices {takes no choices} default {takes no default} other {takes no longest answer}}.");

    public static readonly MessageTemplate SequenceStepApplicationGone = Define(
        "sequence.stepApplicationGone",
        "{step} names an application or bundle that is no longer in the library.");

    public static readonly MessageTemplate SequenceInputApplicationGone = Define(
        "sequence.inputApplicationGone",
        "{input} offers an application or bundle that is no longer in the library.");

    public static readonly MessageTemplate SequenceWingetNeedsSession = Define(
        "sequence.wingetNeedsSession",
        "{application} installs with winget, which needs a Write answer file step.");

    public static readonly MessageTemplate SequenceApplicationsVariableSetBySteps = Define(
        "sequence.applicationsVariableSetBySteps",
        "Steps set {name}, but applications are chosen when the run starts.");

    // Why a run cannot install a step's applications. A run that starts says them in English.

    public static readonly MessageTemplate DeploymentApplicationUnknown = Define(
        "deployment.applicationUnknown",
        "{step} is to install {name}, which is no application or bundle in the library.");

    public static readonly MessageTemplate DeploymentApplicationDependencyGone = Define(
        "deployment.applicationDependencyGone",
        "{application}, which {step} installs, depends on an application that is no longer in the library.");

    public static readonly MessageTemplate DeploymentApplicationFilesGone = Define(
        "deployment.applicationFilesGone",
        "The files of {application}, which {step} installs, are no longer in the library.");

    public static readonly MessageTemplate DeploymentApplicationCycle = Define(
        "deployment.applicationCycle",
        "The applications {step} installs depend on each other in a circle: {path}.");

    public static readonly MessageTemplate ApplicationBundleInUse = Define(
        "application.bundleInUse",
        "This bundle is used by {users}. Take it out there first.");
}
