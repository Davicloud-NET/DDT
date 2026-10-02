// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Building the boot image on the server.

    public static readonly MessageTemplate BootImageNoHelper = Define(
        "bootImage.noHelper",
        "This server cannot build the boot image itself: the DDT Helper service does not answer. DDT's installer for " +
        "Windows sets it up. On another server, build with Build-BootImage.ps1 on a Windows PC.");

    public static readonly MessageTemplate BootImageBusy = Define(
        "bootImage.busy",
        "A build or an install of the ADK is running already. Wait for it to end.");

    public static readonly MessageTemplate BootImageNoAdk = Define(
        "bootImage.noAdk",
        "The Windows ADK with its Windows PE add-on is not installed on this server. Install it first.");

    public static readonly MessageTemplate BootImageOldAdk = Define(
        "bootImage.oldAdk",
        "The Windows PE add-on on this server is {version}, and the boot image needs {oldest} or later.");

    public static readonly MessageTemplate BootImageKeyboardLayout = Define(
        "bootImage.keyboardLayout",
        "Takes an input locale and a keyboard layout, such as 0407:00000407 for German.");

    public static readonly MessageTemplate BootImageWindowSize = Define(
        "bootImage.windowSize",
        "Must be between 1 and {max}.");

    public static readonly MessageTemplate BootImageNoSuchBuild = Define(
        "bootImage.noSuchBuild",
        "The boot directory holds no such build.");

    // Netboot next to this computer's DHCP server and WDS.

    public static readonly MessageTemplate NetbootNoHelper = Define(
        "netboot.noHelper",
        "DDT cannot change this computer's DHCP server or WDS itself: the DDT Helper service does not answer. DDT's installer for Windows sets it up.");

    public static readonly MessageTemplate NetbootHelperFailed = Define("netboot.helperFailed", "The DDT Helper service could not do it: {reason}");

    public static readonly MessageTemplate NetbootScopes = Define("netboot.scopes", "Choose the scopes to set, each by its address.");

    // The builder for another PC, and what it uploads.

    public static readonly MessageTemplate BootImageNoBuilder = Define(
        "bootImage.noBuilder",
        "This server came without the build script or the agent, so it has no builder to hand out. A release has both.");

    public static readonly MessageTemplate BootImageNoRoot = Define(
        "bootImage.noRoot",
        "DDT has no root certificate of its own yet, which the boot image has to trust. Generate one under Server, Certificate.");

    public static readonly MessageTemplate BootImageUploadToken = Define(
        "bootImage.uploadToken",
        "The builder's token is unknown, has expired or uploaded a boot image already. Download the builder again.");

    public static readonly MessageTemplate BootImageUploadTooLarge = Define(
        "bootImage.uploadTooLarge",
        "The boot image is larger than {max} MB.");

    public static readonly MessageTemplate BootImageUploadMissingFile = Define(
        "bootImage.uploadMissingFile",
        "The upload is no boot image build: it has no {file}.");

    public static readonly MessageTemplate BootImageUploadStrayFile = Define(
        "bootImage.uploadStrayFile",
        "The upload is no boot image build: {file} is outside Boot, EFI and x64.");

    public static readonly MessageTemplate BootImageUploadBroken = Define(
        "bootImage.uploadBroken",
        "The upload is no boot image build: its zip, its boot.wim or its description cannot be read.");
}
