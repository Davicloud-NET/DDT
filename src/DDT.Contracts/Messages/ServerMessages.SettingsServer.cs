// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Settings: the server certificate, and the agent that netbooting machines run.

    public static readonly MessageTemplate SettingsCertificateNotManageable = Define(
        "settings.certificate.notManageable",
        "The page manages the certificate only when Kestrel:Certificates:Default:Path and KeyPath both name PEM files and no Password " +
        "is set. A PFX, a key under a password, or TLS at a proxy is managed by hand.");

    public static readonly MessageTemplate SettingsCertificateGenerateOff = Define(
        "settings.certificate.generateOff",
        "DDT:Https:GenerateSelfSignedCertificate is false, so DDT issues no certificate. Upload one instead.");

    public static readonly MessageTemplate SettingsCertificateAddHostFirst = Define(
        "settings.certificate.addHostFirst",
        "Add {host}, the name this page is reached by, to the server names first.");

    public static readonly MessageTemplate SettingsCertificateNewRootUpload = Define(
        "settings.certificate.newRootUpload",
        "This certificate does not come from DDT's root, which every boot image pins: build every boot image again with its root, and " +
        "trust that root in the browsers that manage DDT.");

    public static readonly MessageTemplate SettingsCertificateNewRootGenerate = Define(
        "settings.certificate.newRootGenerate",
        "DDT has no root yet, so Generate makes one: build every boot image again with it, and trust it in the browsers that manage DDT.");

    public static readonly MessageTemplate SettingsCertificateNothingToConfirm = Define(
        "settings.certificate.nothingToConfirm",
        "No certificate waits for a confirmation.");

    public static readonly MessageTemplate SettingsCertificateNotServedNew = Define(
        "settings.certificate.notServedNew",
        "This connection was served the certificate before the new one, so it proves nothing about the new one. Load the page again, " +
        "which connects anew, and confirm from there.");

    public static readonly MessageTemplate SettingsCertificateSendPair = Define(
        "settings.certificate.sendPair",
        "Send the certificate and its key, or a PFX.");

    public static readonly MessageTemplate SettingsCertificateNotBase64 = Define("settings.certificate.notBase64", "Is not base64.");

    public static readonly MessageTemplate SettingsCertificatePfxWithoutKey = Define(
        "settings.certificate.pfxWithoutKey",
        "Holds no certificate with its private key.");

    public static readonly MessageTemplate SettingsCertificateKeyAlgorithm = Define(
        "settings.certificate.keyAlgorithm",
        "Its key is neither RSA nor ECDSA.");

    public static readonly MessageTemplate SettingsCertificatePfxPassword = Define(
        "settings.certificate.pfxPassword",
        "Does not open with this password: {error}");

    public static readonly MessageTemplate SettingsCertificateSendPem = Define(
        "settings.certificate.sendPem",
        "Send the certificate with its intermediates and its key as PEM, or a PFX.");

    public static readonly MessageTemplate SettingsCertificateKeyMismatch = Define(
        "settings.certificate.keyMismatch",
        "The certificate does not load with this key: {error}");

    public static readonly MessageTemplate SettingsCertificateNotValidNow = Define(
        "settings.certificate.notValidNow",
        "It is valid from {from} to {until}, not now.");

    public static readonly MessageTemplate SettingsCertificateMissingNames = Define(
        "settings.certificate.missingNames",
        "It does not name {names}, which the server is reached by, so browsers and agents would refuse it.");

    public static readonly MessageTemplate SettingsAgentConfigured = Define(
        "settings.agent.configured",
        "DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.");

    public static readonly MessageTemplate SettingsAgentNotExecutable = Define(
        "settings.agent.notExecutable",
        "That is not a Windows executable. Upload ddt-agent.exe as Publish-Agent.ps1 builds it.");

    public static readonly MessageTemplate SettingsAgentTooLarge = Define("settings.agent.tooLarge", "The agent may be at most {max} MB.");

    public static readonly MessageTemplate SettingsConsoleConfigured = Define(
        "settings.console.configured",
        "DDT:Agent:ConsolePath names the console in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.");

    public static readonly MessageTemplate SettingsConsoleNotAPackage = Define(
        "settings.console.notAPackage",
        "That is not the console. Upload a zip of the folder Publish-Console.ps1 writes, with ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll and nothing else.");

    public static readonly MessageTemplate SettingsConsoleTooLarge = Define(
        "settings.console.tooLarge",
        "The console may be at most {max} MB, zipped and unpacked.");

    public static readonly MessageTemplate SettingsConsoleLogoNotPng = Define(
        "settings.consoleLogo.notPng",
        "That is not a PNG image. Upload the logo as a PNG file.");

    public static readonly MessageTemplate SettingsConsoleLogoDimensions = Define(
        "settings.consoleLogo.dimensions",
        "The logo is {width} by {height} pixels. It may be at most {max} pixels wide and high.");

    public static readonly MessageTemplate SettingsConsoleLogoTooLarge = Define("settings.consoleLogo.tooLarge", "The logo may be at most {max} KB.");

    // Settings: how a host applied a section that rebuilds a subsystem. An exception's own text stays English, as a value.

    public static readonly MessageTemplate SettingsApplyProxiesClosed = Define(
        "settings.apply.proxiesClosed",
        "No proxy is trusted on this host while the section has problems: {problems}");

    public static readonly MessageTemplate SettingsApplyOidcClosed = Define(
        "settings.apply.oidcClosed",
        "Single sign-on is off on this host while the section has problems: {problems}");

    public static readonly MessageTemplate SettingsApplyOidcFailed = Define(
        "settings.apply.oidcFailed",
        "Single sign-on is off on this host: {error}");

    public static readonly MessageTemplate SettingsApplyPxeClosed = Define(
        "settings.apply.pxeClosed",
        "The pxe settings have problems, so nothing is served until they are fixed: {problems}");

    // Error is the name of the socket error, which also chooses the advice.
    public static readonly MessageTemplate SettingsApplyPxeBindFailed = Define(
        "settings.apply.pxeBindFailed",
        "DDT could not bind UDP {port} for {protocol, select, proxyDhcp {ProxyDHCP} bootServer {PXE boot server} tftp {TFTP} " +
        "tftpSinglePort {TFTP (single port)} other {{protocol}}} ({error}). {error, select, AccessDenied {The process may not bind a " +
        "privileged port. Grant NET_BIND_SERVICE, as build/compose.yaml does.} AddressAlreadyInUse {Another DHCP, PXE or TFTP " +
        "service already holds this port on this host. Stop it, or run DDT without the pxe role here.} other {Check that no other " +
        "service holds the port.}}");
}
