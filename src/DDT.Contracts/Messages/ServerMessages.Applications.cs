// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Applications and bundles in the library. Names and descriptions use NameLength and DescriptionLength, exit codes
    // and the timeout the Run script step's messages. In the messages, application is an application's name.

    public static readonly MessageTemplate ApplicationNameSemicolon = Define(
        "application.nameSemicolon",
        "The name cannot contain a semicolon.");

    public static readonly MessageTemplate ApplicationNameTaken = Define(
        "application.nameTaken",
        "An application or bundle is already called {name}.");

    public static readonly MessageTemplate ApplicationPublisherLength = Define(
        "application.publisherLength",
        "The publisher can have at most {max} characters and no control characters.");

    public static readonly MessageTemplate ApplicationVersionLength = Define(
        "application.versionLength",
        "The version can have at most {max} characters and no control characters.");

    public static readonly MessageTemplate ApplicationKindChoose = Define("application.kind", "Choose the kind of application.");

    public static readonly MessageTemplate ApplicationCommandRequired = Define(
        "application.commandRequired",
        "Enter the command that installs the application.");

    public static readonly MessageTemplate ApplicationCommandLength = Define(
        "application.commandLength",
        "The command can have at most {max} characters and no line breaks.");

    public static readonly MessageTemplate ApplicationCommandNotUsed = Define(
        "application.commandNotUsed",
        "{kind, select, Msix {An MSIX package} other {A winget package}} needs no command. Leave it empty.");

    public static readonly MessageTemplate ApplicationFilesRequired = Define(
        "application.filesRequired",
        "Upload the MSIX package.");

    public static readonly MessageTemplate ApplicationFilesNotUsed = Define(
        "application.filesNotUsed",
        "A winget package has no files. Remove them.");

    public static readonly MessageTemplate ApplicationWingetId = Define(
        "application.wingetId",
        "Enter the winget ID, such as Mozilla.Firefox, in at most {max} characters.");

    public static readonly MessageTemplate ApplicationWingetVersion = Define(
        "application.wingetVersion",
        "Enter the version as winget shows it, in at most {max} characters, or leave it empty.");

    public static readonly MessageTemplate ApplicationWingetNotUsed = Define(
        "application.wingetNotUsed",
        "Only a winget package has a winget ID and version. Leave them empty.");

    public static readonly MessageTemplate ApplicationProductCode = Define(
        "application.productCode",
        "Enter the product code as a GUID, such as {example}.");

    public static readonly MessageTemplate ApplicationUninstallKey = Define(
        "application.uninstallKey",
        "Enter the key name under Uninstall without a path, in at most {max} characters.");

    public static readonly MessageTemplate ApplicationDetectionPath = Define(
        "application.detectionPath",
        "Enter the full path of a file, such as {example}, in at most {max} characters.");

    public static readonly MessageTemplate ApplicationDetectionVersion = Define(
        "application.detectionVersion",
        "Enter a version of 1 to 4 numbers up to 65535, such as 1.2.3.4, or leave it empty.");

    public static readonly MessageTemplate ApplicationPackageFamily = Define(
        "application.packageFamily",
        "Enter the package family name, such as {example}.");

    public static readonly MessageTemplate ApplicationMsixNone = Define(
        "application.msixNone",
        "The files hold no MSIX or Appx package.");

    public static readonly MessageTemplate ApplicationMsixOnlyFrameworks = Define(
        "application.msixOnlyFrameworks",
        "The files hold only framework packages. Add the package that uses them.");

    public static readonly MessageTemplate ApplicationMsixTwoMains = Define(
        "application.msixTwoMains",
        "The files hold more than one main package: {packages}. Keep one.");

    public static readonly MessageTemplate ApplicationMsixTwoLicenses = Define(
        "application.msixTwoLicenses",
        "The files hold more than one licence: {files}. Keep one.");

    public static readonly MessageTemplate ApplicationMsixUnreadable = Define(
        "application.msixUnreadable",
        "{file} is no MSIX or Appx package that can be read.");

    public static readonly MessageTemplate ApplicationMsixTooMany = Define(
        "application.msixTooMany",
        "The files hold more than {max} packages.");

    public static readonly MessageTemplate ApplicationDependsOnItself = Define(
        "application.dependsOnItself",
        "An application cannot depend on itself.");

    public static readonly MessageTemplate ApplicationTooManyDependencies = Define(
        "application.tooManyDependencies",
        "An application can depend on at most {max} others.");

    public static readonly MessageTemplate ApplicationListedTwice = Define(
        "application.listedTwice",
        "This application is listed twice.");

    public static readonly MessageTemplate ApplicationGone = Define(
        "application.gone",
        "This application is no longer in the library.");

    public static readonly MessageTemplate ApplicationDependencyGone = Define(
        "application.dependencyGone",
        "{application} depends on an application that is no longer in the library.");

    public static readonly MessageTemplate ApplicationDependencyCycle = Define(
        "application.dependencyCycle",
        "These applications depend on each other in a circle: {path}.");

    public static readonly MessageTemplate ApplicationBundleMembers = Define(
        "application.bundleMembers",
        "A bundle holds 1 to {max} applications.");

    public static readonly MessageTemplate ApplicationFilesGone = Define(
        "application.filesGone",
        "The uploaded files are no longer on the server. Upload them again.");

    public static readonly MessageTemplate ApplicationInUse = Define(
        "application.inUse",
        "This application is used by {users}. Take it out there first.");

    public static readonly MessageTemplate ApplicationFilesInUse = Define(
        "application.filesInUse",
        "Machines are installing these files or are waiting to. Cancel those runs or let them finish first.");

    public static readonly MessageTemplate ApplicationChangedMeanwhile = Define(
        "application.changedMeanwhile",
        "Someone changed it meanwhile. Look at it again.");
}
