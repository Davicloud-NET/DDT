// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Accounts, their passwords, second factors and API tokens.

    public static readonly MessageTemplate UserNameEmpty = Define("user.nameEmpty", "Enter a user name.");

    public static readonly MessageTemplate UserNameTooLong = Define("user.nameTooLong", "A user name can have at most {max} characters.");

    public static readonly MessageTemplate UserDisplayNameEmpty = Define(
        "user.displayNameEmpty",
        "Enter the name DDT shows for the account.");

    public static readonly MessageTemplate UserDisplayNameTooLong = Define(
        "user.displayNameTooLong",
        "A display name can have at most {max} characters.");

    public static readonly MessageTemplate UserEmail = Define(
        "user.email",
        "Enter an email address such as jane@corp.example, or leave it empty.");

    public static readonly MessageTemplate UserRole = Define("user.role", "Choose Administrator, Operator or Viewer.");

    public static readonly MessageTemplate UserNameTaken = Define("user.nameTaken", "There is an account named {name} already.");

    public static readonly MessageTemplate UserDirectoryProvidesNames = Define(
        "user.directoryProvidesNames",
        "The directory provides the display name and email address of {name}. Change them there; DDT takes them over at its next " +
        "sign-in.");

    public static readonly MessageTemplate UserRoleFromDirectoryGroups = Define(
        "user.roleFromDirectoryGroupMap",
        "The role of {name} comes from its directory groups through the directory's group map, at each sign-in. Change its groups in " +
        "the directory, or the map on the Sign-in page.");

    public static readonly MessageTemplate UserRoleFromSingleSignOnGroups = Define(
        "user.roleFromSingleSignOnGroupMap",
        "The role of {name} comes from its single sign-on groups through the single sign-on group map, at each sign-in. Change its " +
        "groups at the provider, or the map on the Sign-in page.");

    public static readonly MessageTemplate UserOwnAdministratorRole = Define(
        "user.ownAdministratorRole",
        "You cannot take the Administrator role from your own account. Another administrator can.");

    public static readonly MessageTemplate UserLastAdministrator = Define(
        "user.lastAdministrator",
        "{name} is the last enabled administrator. Make another account an administrator first.");

    public static readonly MessageTemplate UserOwnDisable = Define(
        "user.ownDisable",
        "You cannot disable your own account. Another administrator can.");

    public static readonly MessageTemplate UserOwnPassword = Define("user.ownPassword", "Change your own password on the Account page.");

    public static readonly MessageTemplate UserDirectoryPassword = Define(
        "user.directoryPassword",
        "{name} signs in with its directory password. Reset it in the directory.");

    public static readonly MessageTemplate UserSingleSignOnPassword = Define(
        "user.singleSignOnPassword",
        "{name} signs in through single sign-on and has no password in DDT.");

    public static readonly MessageTemplate UserOwnSecondFactor = Define(
        "user.ownSecondFactor",
        "Turn off your own second factor on the Account page.");

    public static readonly MessageTemplate UserOwnDelete = Define(
        "user.ownDelete",
        "You cannot delete your own account. Another administrator can.");

    public static readonly MessageTemplate UserChangedWhileSaving = Define(
        "user.changedWhileSaving",
        "The account changed while this was saved. Look at it again.");

    public static readonly MessageTemplate AccountPasswordInDirectory = Define(
        "account.passwordInDirectory",
        "This account is managed by the directory. Change the password there.");

    public static readonly MessageTemplate AccountNoPassword = Define(
        "account.noPassword",
        "This account signs in through single sign-on and has no password in DDT.");

    public static readonly MessageTemplate AccountCodeNotValidCheckTime = Define(
        "account.codeNotValidCheckTime",
        "That code is not valid. Check the time on the device generating it.");

    public static readonly MessageTemplate AccountCodeNotValid = Define("account.codeNotValid", "That code is not valid.");

    public static readonly MessageTemplate AccountNoExternalSignIn = Define(
        "account.noExternalSignIn",
        "No external sign in is in progress.");

    public static readonly MessageTemplate AccountApiTokenCannotChange = Define(
        "account.apiTokenCannotChange",
        "An API token cannot change the account it belongs to. Sign in on the web to do this.");

    public static readonly MessageTemplate TokenNameTaken = Define(
        "token.nameTaken",
        "You have a token of that name already. Choose another name, or revoke that token first.");

    public static readonly MessageTemplate TokenNoRole = Define("token.noRole", "Your account has no role, so it cannot have a token.");

    public static readonly MessageTemplate TokenRoleTooHigh = Define("token.roleTooHigh", "A token can do at most what you can, and you are {role}.");

    public static readonly MessageTemplate TokenLifetime = Define("token.lifetime", "A token lasts 1 to {max} days.");

    public static readonly MessageTemplate TokenOnlyOwnerRevokes = Define(
        "token.onlyOwnerRevokes",
        "Only the token's owner or an administrator can revoke it.");

    public static readonly MessageTemplate AuditRangeEnd = Define("audit.rangeEnd", "The end of the range must come after its start.");

    // What ASP.NET Core Identity refuses, in Identity's own English. DdtIdentityErrorDescriber hands these messages to
    // Identity.

    public static readonly MessageTemplate IdentityDefaultError = Define("identity.defaultError", "An unknown failure has occurred.");

    public static readonly MessageTemplate IdentityConcurrencyFailure = Define(
        "identity.concurrencyFailure",
        "Optimistic concurrency failure, object has been modified.");

    public static readonly MessageTemplate IdentityPasswordMismatch = Define("identity.passwordMismatch", "Incorrect password.");

    public static readonly MessageTemplate IdentityInvalidToken = Define("identity.invalidToken", "Invalid token.");

    public static readonly MessageTemplate IdentityRecoveryCodeRedemptionFailed = Define(
        "identity.recoveryCodeRedemptionFailed",
        "Recovery code redemption failed.");

    public static readonly MessageTemplate IdentityLoginAlreadyAssociated = Define(
        "identity.loginAlreadyAssociated",
        "A user with this login already exists.");

    public static readonly MessageTemplate IdentityInvalidUserName = Define(
        "identity.invalidUserName",
        "Username ''{name}'' is invalid, can only contain letters or digits.");

    public static readonly MessageTemplate IdentityInvalidEmail = Define("identity.invalidEmail", "Email ''{email}'' is invalid.");

    public static readonly MessageTemplate IdentityDuplicateUserName = Define(
        "identity.duplicateUserName",
        "Username ''{name}'' is already taken.");

    public static readonly MessageTemplate IdentityDuplicateEmail = Define(
        "identity.duplicateEmail",
        "Email ''{email}'' is already taken.");

    public static readonly MessageTemplate IdentityInvalidRoleName = Define("identity.invalidRoleName", "Role name ''{role}'' is invalid.");

    public static readonly MessageTemplate IdentityDuplicateRoleName = Define(
        "identity.duplicateRoleName",
        "Role name ''{role}'' is already taken.");

    public static readonly MessageTemplate IdentityUserAlreadyHasPassword = Define(
        "identity.userAlreadyHasPassword",
        "User already has a password set.");

    public static readonly MessageTemplate IdentityUserLockoutNotEnabled = Define(
        "identity.userLockoutNotEnabled",
        "Lockout is not enabled for this user.");

    public static readonly MessageTemplate IdentityUserAlreadyInRole = Define(
        "identity.userAlreadyInRole",
        "User already in role ''{role}''.");

    public static readonly MessageTemplate IdentityUserNotInRole = Define("identity.userNotInRole", "User is not in role ''{role}''.");

    public static readonly MessageTemplate IdentityPasswordTooShort = Define(
        "identity.passwordTooShort",
        "Passwords must be at least {length} characters.");

    public static readonly MessageTemplate IdentityPasswordRequiresUniqueChars = Define(
        "identity.passwordRequiresUniqueChars",
        "Passwords must use at least {count} different characters.");

    public static readonly MessageTemplate IdentityPasswordRequiresNonAlphanumeric = Define(
        "identity.passwordRequiresNonAlphanumeric",
        "Passwords must have at least one non alphanumeric character.");

    public static readonly MessageTemplate IdentityPasswordRequiresDigit = Define(
        "identity.passwordRequiresDigit",
        "Passwords must have at least one digit ('0'-'9').");

    public static readonly MessageTemplate IdentityPasswordRequiresLower = Define(
        "identity.passwordRequiresLower",
        "Passwords must have at least one lowercase ('a'-'z').");

    public static readonly MessageTemplate IdentityPasswordRequiresUpper = Define(
        "identity.passwordRequiresUpper",
        "Passwords must have at least one uppercase ('A'-'Z').");

    // An error that Identity created some other way, in its own English.
    public static readonly MessageTemplate IdentityOther = Define("identity.other", "{description}");
}
