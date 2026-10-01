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
}
