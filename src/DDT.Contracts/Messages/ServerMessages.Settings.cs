// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Settings: stored values and secrets this server cannot read, and a save that needs more than the values.

    public static readonly MessageTemplate SettingsValuesCannotBeChecked = Define(
        "settings.valuesCannotBeChecked",
        "The values cannot be checked: {error}");

    public static readonly MessageTemplate SettingsConfigurationUnreadable = Define(
        "settings.configurationUnreadable",
        "{section} cannot be read from configuration: {error}");

    public static readonly MessageTemplate SettingsStoredSecretUnreadable = Define(
        "settings.storedSecretUnreadable",
        "The stored value no longer decrypts with this server's key ring. Enter it again.");

    public static readonly MessageTemplate SettingsStoredValueUnreadable = Define(
        "settings.storedValueUnreadable",
        "The stored value cannot be read, so the default applies: {error}");

    public static readonly MessageTemplate SettingsSecretCannotBeKept = Define(
        "settings.secretCannotBeKept",
        "No longer decrypts with this server's key ring, so it cannot be kept. Enter it again or clear it.");

    public static readonly MessageTemplate SettingsSecretForNewServer = Define(
        "settings.secretForNewServer",
        "Enter it again for the new server: a stored secret goes only to the server it was entered for.");

    public static readonly MessageTemplate SettingsLdapTestOwnSignIn = Define(
        "settings.ldap.testOwnSignIn",
        "You sign in through the directory, and these values decide whether you still can. Test your own sign-in with them first; " +
        "the save is accepted while a test that kept you an administrator is less than 5 minutes old.");

    // Settings: warnings. A save must confirm the ones that have a confirmation code. The others are only information.

    public static readonly MessageTemplate SettingsNoLocalAdministrator = Define(
        "settings.noLocalAdministrator",
        "No local administrator account is enabled. Should the directory or the provider stop granting the Administrator role, only " +
        "the console command settings create-admin could let anyone in again.");

    public static readonly MessageTemplate SettingsMachinesNetworkWide = Define(
        "settings.machines.networkWide",
        "{network} is wider than a /{prefix}. Every address in it counts as a zero touch address.");

    public static readonly MessageTemplate SettingsProxiesNetworkWide = Define(
        "settings.proxies.networkWide",
        "{network} is wider than a /{prefix}. Every address in it counts as a trusted proxy address.");

    public static readonly MessageTemplate SettingsLdapUnencrypted = Define(
        "settings.ldap.unencrypted",
        "The bind password and every password typed at sign-in cross the network in clear text.");

    public static readonly MessageTemplate SettingsLdapRekey = Define(
        "settings.ldap.rekey",
        "Every directory account is keyed on {current}. With {next} each one is taken for a new person at its next sign-in, and its " +
        "roles and history stay with the old account.");

    public static readonly MessageTemplate SettingsLdapNoAdministrator = Define(
        "settings.ldap.noAdministrator",
        "No group maps to Administrator, so every directory account that is an administrator loses the role at its next sign-in.");

    public static readonly MessageTemplate SettingsOidcOperatorRole = Define(
        "settings.oidc.operatorRole",
        "Every identity the provider signs in that DDT has not seen becomes an operator, and operators can read the deployment " +
        "passwords by deploying a machine they control.");

    public static readonly MessageTemplate SettingsPxeBootUrl = Define(
        "settings.pxe.bootUrl",
        "{url} is not where DDT serves boot files, which is http://<server>:{port}/boot/. Keep it only if another server serves this file.");

    public static readonly MessageTemplate SettingsPxeInterfaceNotFound = Define(
        "settings.pxe.interfaceNotFound",
        "''{name}'' names no interface on {host}, which serves nothing for it.");

    // Settings: saving a section, and proving who you are again.

    public static readonly MessageTemplate SettingsSendValues = Define("settings.sendValues", "Send the values of the section.");

    public static readonly MessageTemplate SettingsNoSuchSecret = Define("settings.noSuchSecret", "{section} has no secret called {name}.");

    public static readonly MessageTemplate SettingsSavedSince = Define(
        "settings.savedSince",
        "Someone saved {section} since you loaded it. Load it again.");

    public static readonly MessageTemplate SettingsEnterPasswordAgain = Define(
        "settings.enterPasswordAgain",
        "Enter your password again to change {fields}.");

    public static readonly MessageTemplate SettingsKeyRingUnreadable = Define(
        "settings.keyRingUnreadable",
        "This server cannot read the key ring the stored settings secrets were encrypted with, so it saves no settings. Every DDT " +
        "process on one database has to share the key ring in DDT:StorePath/keys.");

    public static readonly MessageTemplate SettingsReauthenticateNoPassword = Define(
        "settings.reauthenticate.noPassword",
        "This account signs in without a password DDT can check, so it cannot change these settings. Use an account with a local or " +
        "directory password.");

    public static readonly MessageTemplate SettingsReauthenticateLockedOut = Define(
        "settings.reauthenticate.lockedOut",
        "The account is locked out. Try again later.");

    public static readonly MessageTemplate SettingsReauthenticateCodeNeeded = Define(
        "settings.reauthenticate.codeNeeded",
        "Enter the code of your authenticator as well.");

    public static readonly MessageTemplate SettingsReauthenticateNotRight = Define(
        "settings.reauthenticate.notRight",
        "The password or the code is not right.");

    // Settings: testing the directory and the single sign-on provider with values not yet saved.

    public static readonly MessageTemplate SettingsLdapTestSendValues = Define("settings.ldapTest.sendValues", "Send the values to test.");

    public static readonly MessageTemplate SettingsLdapTestNotDirectoryAccount = Define(
        "settings.ldapTest.notDirectoryAccount",
        "{name} is not a directory account, so its password is not sent to the directory.");

    public static readonly MessageTemplate SettingsLdapTestAccountDisabled = Define(
        "settings.ldapTest.accountDisabled",
        "{name} is disabled, so a sign-in is refused before the directory is asked.");

    public static readonly MessageTemplate SettingsLdapTestLockedOut = Define(
        "settings.ldapTest.lockedOut",
        "{name} is locked out, so a sign-in is refused before the directory is asked.");

    public static readonly MessageTemplate SettingsLdapTestBindFailed = Define(
        "settings.ldapTest.bindFailed",
        "The bind as {account} to {server} failed: {error}");

    public static readonly MessageTemplate SettingsLdapTestBound = Define(
        "settings.ldapTest.bound",
        "The bind as {account} to {server} succeeded.");

    public static readonly MessageTemplate SettingsLdapTestManyEntries = Define(
        "settings.ldapTest.manyEntries",
        "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestNoEntry = Define(
        "settings.ldapTest.noEntry",
        "No entry under {baseDn} matches {name} with the user filter.");

    public static readonly MessageTemplate SettingsLdapTestPasswordRefused = Define(
        "settings.ldapTest.passwordRefused",
        "{entry} was found, but the directory refused the password.");

    public static readonly MessageTemplate SettingsLdapTestNoImmutableId = Define(
        "settings.ldapTest.noImmutableId",
        "{entry} has no {attribute}, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestFound = Define(
        "settings.ldapTest.found",
        "{entry} was found, in {count, plural, one {# group} other {# groups}}.");

    public static readonly MessageTemplate SettingsLdapTestSearchFailed = Define(
        "settings.ldapTest.searchFailed",
        "The search for {name} under {baseDn} failed: {error}");

    // Result is the directory's answer, as a nested message.
    public static readonly MessageTemplate SettingsLdapTestNoRole = Define(
        "settings.ldapTest.noRole",
        "{result} The group map gives no role, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestRole = Define(
        "settings.ldapTest.role",
        "{result} The group map makes the account {role}.");

    public static readonly MessageTemplate SettingsOidcTestAuthorityInvalid = Define(
        "settings.oidcTest.authorityInvalid",
        "Enter the provider's https address, such as https://login.example.com/realms/ddt.");

    public static readonly MessageTemplate SettingsOidcTestAnswered = Define(
        "settings.oidcTest.answered",
        "{url} answered {status} {reason}.");

    public static readonly MessageTemplate SettingsOidcTestNotDiscovery = Define(
        "settings.oidcTest.notDiscovery",
        "{url} is not the discovery document of an OpenID Connect provider.");

    public static readonly MessageTemplate SettingsOidcTestReached = Define(
        "settings.oidcTest.reached",
        "The provider answered as {issuer}. Register {redirectUri} as the redirect URI of DDT's client there.");

    public static readonly MessageTemplate SettingsOidcTestOtherIssuer = Define(
        "settings.oidcTest.otherIssuer",
        "The provider names itself {issuer}, not {authority}. Enter {issuer} as the authority.");

    public static readonly MessageTemplate SettingsOidcTestUnreadable = Define(
        "settings.oidcTest.unreadable",
        "{url} could not be read: {error}");
}
