// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Accounts that steps use (Deployment > Accounts), accounts given for one run, and the problems of sequences that
    // name them. In the messages, account is an account's name, input an input's name and host a share's server.

    public static readonly MessageTemplate StepAccountNameTaken = Define(
        "stepAccount.nameTaken",
        "Another account is already called {name}. Choose another name.");

    public static readonly MessageTemplate StepAccountUserNameForm = Define(
        "stepAccount.userNameForm",
        "Enter the user name with its domain, as DOMAIN\\user or user@corp.example, in at most {max} characters.");

    public static readonly MessageTemplate StepAccountDomainInvalid = Define(
        "stepAccount.domainInvalid",
        "''{value}'' is not a domain name. Enter one such as corp.example, or leave it empty.");

    public static readonly MessageTemplate StepAccountHostInvalid = Define(
        "stepAccount.hostInvalid",
        "''{value}'' is not a server name. Enter it as share paths name it, such as files.corp.example.");

    public static readonly MessageTemplate StepAccountHostRepeated = Define("stepAccount.hostRepeated", "{value} is listed twice.");

    public static readonly MessageTemplate StepAccountTooManyHosts = Define(
        "stepAccount.tooManyHosts",
        "An account can name at most {max} servers.");

    public static readonly MessageTemplate StepAccountPasswordRequired = Define("stepAccount.passwordRequired", "Enter the password.");

    public static readonly MessageTemplate StepAccountPasswordLength = Define(
        "stepAccount.passwordLength",
        "The password can have at most {max} characters.");

    public static readonly MessageTemplate StepAccountPasswordForNewDestination = Define(
        "stepAccount.passwordForNewDestination",
        "Enter the password again: a stored password goes only to the user name, domain and servers it was entered for.");

    public static readonly MessageTemplate StepAccountPasswordUnreadable = Define(
        "stepAccount.passwordUnreadable",
        "The stored password no longer decrypts with this server's key ring, so it cannot be kept. Enter it again or clear it.");

    public static readonly MessageTemplate StepAccountInUse = Define(
        "stepAccount.inUse",
        "{count, plural, one {The sequence {sequences} uses this account. Choose another account there first.} " +
        "other {The sequences {sequences} use this account. Choose another account there first.}}");

    public static readonly MessageTemplate StepAccountChangedMeanwhile = Define(
        "stepAccount.changedMeanwhile",
        "Someone changed the account meanwhile. Look at it again.");

    public static readonly MessageTemplate StepAccountReauthenticate = Define(
        "stepAccount.reauthenticate",
        "Enter your password again to change the accounts that steps use.");

    public static readonly MessageTemplate StepAccountApiToken = Define(
        "stepAccount.apiToken",
        "An API token cannot change the accounts that steps use. Sign in on the web to do this.");

    public static readonly MessageTemplate SequenceAccountGone = Define(
        "sequence.accountGone",
        "The account is no longer on the Accounts page. Choose another one.");

    public static readonly MessageTemplate SequenceAccountNoPassword = Define(
        "sequence.accountNoPassword",
        "The account {account} has no password. Set it on the Accounts page.");

    public static readonly MessageTemplate SequenceAccountPasswordUnreadable = Define(
        "sequence.accountPasswordUnreadable",
        "The password of the account {account} no longer decrypts with this server's key ring. Enter it again on the Accounts page.");

    public static readonly MessageTemplate SequenceAccountNoDomain = Define(
        "sequence.accountNoDomain",
        "The account {account} names no domain, so it cannot join one. Enter its domain on the Accounts page, or choose another account.");

    public static readonly MessageTemplate SequenceAccountNoRunAs = Define(
        "sequence.accountNoRunAs",
        "The account {account} does not let scripts run as it. Allow that on the Accounts page, or choose another account.");

    public static readonly MessageTemplate SequenceAccountHostNotAllowed = Define(
        "sequence.accountHostNotAllowed",
        "The account {account} may not connect to {host}. Add the server to it on the Accounts page, or choose another account.");

    public static readonly MessageTemplate SequenceAccountInputNoDomain = Define(
        "sequence.accountInputNoDomain",
        "The input {input} names no domain, so the account given for it cannot join one. Name the domain in the input.");

    public static readonly MessageTemplate SequenceAccountInputNoRunAs = Define(
        "sequence.accountInputNoRunAs",
        "The input {input} does not let scripts run as the account given for it. Allow that in the input.");

    public static readonly MessageTemplate SequenceAccountInputHostNotAllowed = Define(
        "sequence.accountInputHostNotAllowed",
        "The input {input} does not let the account given for it connect to {host}. Add the server to the input.");
}
