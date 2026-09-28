// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Settings: what a section's fields may not hold, worded for someone looking at the field. Setting names such as
    // Domain:Name are the configuration names.

    public static readonly MessageTemplate SettingsAtLeastOne = Define("settings.atLeastOne", "Must be at least 1.");

    public static readonly MessageTemplate SettingsRoleUnknown = Define("settings.roleUnknown", "''{role}'' is not a DDT role. Use {roles}.");

    public static readonly MessageTemplate SettingsDeploymentTimeZoneUnknown = Define(
        "settings.deployment.timeZoneUnknown",
        "''{value}'' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time, or leave it " +
        "empty so that Windows picks the zone of the locale.");

    public static readonly MessageTemplate SettingsDeploymentLocaleUnknown = Define(
        "settings.deployment.localeUnknown",
        "''{value}'' is not a culture name. Use one such as de-DE or en-US, or leave it empty for the image's own language.");

    public static readonly MessageTemplate SettingsDeploymentConsoleLanguageUnknown = Define(
        "settings.deployment.consoleLanguageUnknown",
        "''{value}'' is not a language the console at the machine speaks. Use en or de, or leave it empty for the language of " +
        "Windows PE.");

    public static readonly MessageTemplate SettingsDeploymentAdministratorNameInvalid = Define(
        "settings.deployment.administratorNameInvalid",
        "''{value}'' is not a valid account name. Use 1 to {max} characters and none of \" / \\ [ ] : ; | = , + * ? < >.");

    public static readonly MessageTemplate SettingsDeploymentDomainUserNameRequired = Define(
        "settings.deployment.domainUserNameRequired",
        "Required when Domain:Name is set. Name the account that joins the machines, as DOMAIN\\user or user@domain.example.");

    public static readonly MessageTemplate SettingsDeploymentDomainUserNameForm = Define(
        "settings.deployment.domainUserNameForm",
        "''{value}'' must be written as DOMAIN\\user or user@domain.example.");

    public static readonly MessageTemplate SettingsDeploymentRequiredWithDomain = Define(
        "settings.deployment.requiredWithDomain",
        "Required when Domain:Name is set.");

    public static readonly MessageTemplate SettingsDeploymentAdministratorPasswordRequired = Define(
        "settings.deployment.administratorPasswordRequired",
        "Required when Domain:Name is set. Without a local administrator, a domain machine stops at the account page of its first start.");

    public static readonly MessageTemplate SettingsDeploymentControllerInvalid = Define(
        "settings.deployment.controllerInvalid",
        "''{value}'' is not a host name or an address. Name the domain controller alone, such as dc1.corp.example or 10.0.0.10, " +
        "without a scheme or a port.");

    public static readonly MessageTemplate SettingsMachinesPerAddressRange = Define(
        "settings.machines.perAddressRange",
        "Must be between 1 and MaxWaiting, which is {max}.");

    public static readonly MessageTemplate SettingsMachinesNetworkInvalid = Define(
        "settings.machines.networkInvalid",
        "''{value}'' is not a network. Write each one as an address and a prefix length with no address bits set after the prefix, " +
        "such as 10.20.0.0/16, fd00:20::/64 or 10.20.1.5/32 for one machine.");

    public static readonly MessageTemplate SettingsMachinesNetworkEverything = Define(
        "settings.machines.networkEverything",
        "''{value}'' is every address there is. Name the provisioning networks themselves.");

    public static readonly MessageTemplate SettingsMachinesNetworkHoldsProxy = Define(
        "settings.machines.networkHoldsProxy",
        "The zero touch network {network} contains the proxy {proxy}. A request the proxy forwards without the client's address " +
        "would count as one from that network.");

    public static readonly MessageTemplate SettingsMachinesNetworkOverlapsProxies = Define(
        "settings.machines.networkOverlapsProxies",
        "The zero touch network {network} overlaps the proxy network {proxies}. A request a proxy there forwards without the " +
        "client's address would count as one from that network.");

    public static readonly MessageTemplate SettingsProxiesAddressInvalid = Define(
        "settings.proxies.addressInvalid",
        "''{value}'' is not an IP address.");

    public static readonly MessageTemplate SettingsProxiesNetworkInvalid = Define(
        "settings.proxies.networkInvalid",
        "''{value}'' is not a network such as 10.20.0.0/24 with no address bits set past the prefix length.");

    public static readonly MessageTemplate SettingsProxiesNetworkEverything = Define(
        "settings.proxies.networkEverything",
        "''{value}'' is every address there is, so any client could claim any address. Name the proxies' own network.");

    public static readonly MessageTemplate SettingsLdapHostRequired = Define(
        "settings.ldap.hostRequired",
        "Required while directory sign-in is on. Name the directory server, such as dc1.corp.example.");

    public static readonly MessageTemplate SettingsLdapPortInvalid = Define(
        "settings.ldap.portInvalid",
        "{port} is not a port number. LDAPS uses 636, and StartTLS 389.");

    public static readonly MessageTemplate SettingsLdapTransportInvalid = Define(
        "settings.ldap.transportInvalid",
        "Must be Ldaps, StartTls or UnencryptedDangerous.");

    // The placeholder is {0}, but the text uses {placeholder} for it. The catalog's web tests would take a number in
    // braces for an argument that was never named.
    public static readonly MessageTemplate SettingsLdapUserFilterPlaceholder = Define(
        "settings.ldap.userFilterPlaceholder",
        "Must contain {placeholder}, which DDT replaces with the user name, such as (&(objectClass=user)(sAMAccountName={placeholder})).");

    public static readonly MessageTemplate SettingsLdapUserFilterBraces = Define(
        "settings.ldap.userFilterBraces",
        "Is not a valid template: a brace that is not part of {placeholder} has to be written twice, as {open} or {close}.");

    public static readonly MessageTemplate SettingsLdapTimeoutNotPositive = Define(
        "settings.ldap.timeoutNotPositive",
        "Must be longer than zero, such as 00:00:10.");

    public static readonly MessageTemplate SettingsLdapNestedGroupsOff = Define(
        "settings.ldap.nestedGroupsOff",
        "false reads no groups, so every directory user would be refused. Turn it on, or empty GroupRoleMap.");

    public static readonly MessageTemplate SettingsOidcProvisionRoleUnknown = Define(
        "settings.oidc.provisionRoleUnknown",
        "''{role}'' is not a DDT role. Use {viewer} or {operator}.");

    public static readonly MessageTemplate SettingsOidcProvisionAdministrator = Define(
        "settings.oidc.provisionAdministrator",
        "{administrator} would make every identity the provider signs in that DDT has not seen an administrator. Use {viewer} or " +
        "{operator}, or map a group to {administrator} in GroupRoleMap.");

    public static readonly MessageTemplate SettingsOidcGroupsClaimRequired = Define(
        "settings.oidc.groupsClaimRequired",
        "GroupRoleMap needs the claim that carries the groups, such as groups.");

    public static readonly MessageTemplate SettingsOidcScopesWithoutOpenid = Define(
        "settings.oidc.scopesWithoutOpenid",
        "Must contain openid, which is what makes the sign-in OpenID Connect.");

    public static readonly MessageTemplate SettingsOidcAuthorityRequired = Define(
        "settings.oidc.authorityRequired",
        "Required while single sign-on is on: the provider's https address, such as https://login.example.com/realms/ddt.");

    public static readonly MessageTemplate SettingsOidcClientIdRequired = Define(
        "settings.oidc.clientIdRequired",
        "Required while single sign-on is on: the client id the provider shows for DDT.");

    public static readonly MessageTemplate SettingsOidcCannotStart = Define(
        "settings.oidc.cannotStart",
        "Single sign-on cannot start with these values: {error}");

    public static readonly MessageTemplate SettingsOidcSignInScheme = Define(
        "settings.oidc.signInScheme",
        "The handler would sign into ''{scheme}'', which bypasses local account linking entirely.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryEmpty = Define(
        "settings.pxe.bootDirectoryEmpty",
        "Must not be empty. Leave it out for the folder boot in DDT:StorePath.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryIsRoot = Define(
        "settings.pxe.bootDirectoryIsRoot",
        "''{directory}'' is the root of a filesystem, which would serve every file on it. Use a directory of its own, such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryHoldsStore = Define(
        "settings.pxe.bootDirectoryHoldsStore",
        "''{directory}'' holds DDT:StorePath, {store}, which would serve the database and the key ring. Use a directory of its own, " +
        "such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryInKeys = Define(
        "settings.pxe.bootDirectoryInKeys",
        "''{directory}'' is in the key ring folder {keys}, which would serve its keys. Use a directory of its own, such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryHoldsKey = Define(
        "settings.pxe.bootDirectoryHoldsKey",
        "''{directory}'' holds the folder of the TLS certificate or key {file}, which would serve the key. Use a directory of its own, " +
        "such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeNotIpv4Address = Define(
        "settings.pxe.notIpv4Address",
        "''{value}'' is not an IPv4 address.");

    public static readonly MessageTemplate SettingsPxeServerAddressRequired = Define(
        "settings.pxe.serverAddressRequired",
        "Required while EnableTftp is false, because this target uses Tftp. Name the TFTP server that serves it.");

    public static readonly MessageTemplate SettingsPxeHttpBootPortInvalid = Define(
        "settings.pxe.httpBootPortInvalid",
        "{port} is not a port number.");

    public static readonly MessageTemplate SettingsPxeWindowSizeRange = Define("settings.pxe.windowSizeRange", "Must be between 1 and {max}.");

    public static readonly MessageTemplate SettingsPxeArchitectureUnknown = Define(
        "settings.pxe.architectureUnknown",
        "''{value}'' is not a client architecture. Use one of: {architectures}.");

    public static readonly MessageTemplate SettingsPxeMethodInvalid = Define("settings.pxe.methodInvalid", "Must be Tftp or Http.");

    public static readonly MessageTemplate SettingsPxeMethodForArchitecture = Define(
        "settings.pxe.methodForArchitecture",
        "Must be {method} for {architecture} clients.");

    public static readonly MessageTemplate SettingsPxeBootFileRequired = Define("settings.pxe.bootFileRequired", "Must be set.");

    public static readonly MessageTemplate SettingsPxeAsciiMaxLength = Define(
        "settings.pxe.asciiMaxLength",
        "Must be ASCII and at most {max} characters.");

    public static readonly MessageTemplate SettingsPxeBootFileUrl = Define("settings.pxe.bootFileUrl", "Must be an absolute http or https URL.");

    public static readonly MessageTemplate SettingsLoggingLevelUnknown = Define(
        "settings.logging.levelUnknown",
        "''{value}'' is not a log level. Use Trace, Debug, Information, Warning, Error, Critical or None.");

    public static readonly MessageTemplate SettingsCertificateNameInvalid = Define(
        "settings.certificate.nameInvalid",
        "''{name}'' is not a host name or an address. Write each name alone, such as ddt.corp.example or 10.0.0.5.");
}
