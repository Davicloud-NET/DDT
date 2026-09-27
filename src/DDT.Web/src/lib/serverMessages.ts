// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Written by scripts/server-messages.mjs from scripts/server-messages.json, which the server writes from its
// catalog. Change src/DDT.Contracts/Messages/ServerMessages.cs, run the server's tests, then npm run messages.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

export const serverMessages: Readonly<Record<string, MessageDescriptor>> = {
  "account.apiTokenCannotChange": msg({
    context: "account.apiTokenCannotChange",
    message: "An API token cannot change the account it belongs to. Sign in on the web to do this.",
  }),
  "account.codeNotValid": msg({
    context: "account.codeNotValid",
    message: "That code is not valid.",
  }),
  "account.codeNotValidCheckTime": msg({
    context: "account.codeNotValidCheckTime",
    message: "That code is not valid. Check the time on the device generating it.",
  }),
  "account.noExternalSignIn": msg({
    context: "account.noExternalSignIn",
    message: "No external sign in is in progress.",
  }),
  "account.noPassword": msg({
    context: "account.noPassword",
    message: "This account signs in through single sign-on and has no password in DDT.",
  }),
  "account.passwordInDirectory": msg({
    context: "account.passwordInDirectory",
    message: "This account is managed by the directory. Change the password there.",
  }),
  "audit.rangeEnd": msg({
    context: "audit.rangeEnd",
    message: "The end of the range must come after its start.",
  }),
  "common.descriptionLength": msg({
    context: "common.descriptionLength",
    message: "The description can have at most {max} characters.",
  }),
  "common.nameLength": msg({
    context: "common.nameLength",
    message: "The name must have 1 to {max} characters and no control characters.",
  }),
  "common.sentences": msg({
    context: "common.sentences",
    message: "{first} {rest}",
  }),
  "computerName.characters": msg({
    context: "computerName.characters",
    message: "A computer name can hold only the letters A to Z, digits and hyphens.",
  }),
  "computerName.digitsOnly": msg({
    context: "computerName.digitsOnly",
    message: "A computer name cannot consist of digits only.",
  }),
  "computerName.empty": msg({
    context: "computerName.empty",
    message: "Enter a computer name.",
  }),
  "computerName.startsWithHyphen": msg({
    context: "computerName.startsWithHyphen",
    message: "A computer name cannot start with a hyphen.",
  }),
  "computerName.tooLong": msg({
    context: "computerName.tooLong",
    message: "A computer name can have at most {max} characters.",
  }),
  "deployment.alreadyHasRun": msg({
    context: "deployment.alreadyHasRun",
    message: "This machine already has a run. Cancel it before assigning another sequence.",
  }),
  "deployment.approveThenChooseDisk": msg({
    context: "deployment.approveThenChooseDisk",
    message: "{sequence} erases a disk, and this machine has more than one. Approve it without a sequence, then sign in at it and choose the disk there.",
  }),
  "deployment.approveThenName": msg({
    context: "deployment.approveThenName",
    message: "{sequence} {use, select, domain {joins the domain} other {names the machine in its cloud-init seed}}, and this machine has no name yet. Approve it without a sequence, then assign the sequence with a computer name.",
  }),
  "deployment.enterComputerName": msg({
    context: "deployment.enterComputerName",
    message: "Enter a computer name. {use}",
  }),
  "deployment.erasesOneOfManyDisks": msg({
    context: "deployment.erasesOneOfManyDisks",
    message: "{sequence} erases a disk, and this machine has more than one. Sign in at it and choose the disk there.",
  }),
  "deployment.historyCursor": msg({
    context: "deployment.historyCursor",
    message: "The cursor is not one this server handed out. Start again from the first page.",
  }),
  "deployment.joinsDomainUnderName": msg({
    context: "deployment.joinsDomainUnderName",
    message: "The sequence joins the machine to the domain under this name.",
  }),
  "deployment.machineRejected": msg({
    context: "deployment.machineRejected",
    message: "The machine was rejected, so it cannot be given a sequence. Assign the sequence to another machine.",
  }),
  "deployment.machineRetired": msg({
    context: "deployment.machineRetired",
    message: "The machine was retired, so it cannot be given a sequence. Assign the sequence to another machine.",
  }),
  "deployment.machineRunning": msg({
    context: "deployment.machineRunning",
    message: "The machine is running a task sequence. Stop that run before assigning another sequence.",
  }),
  "deployment.notStartingWithSecureBoot": msg({
    context: "deployment.notStartingWithSecureBoot",
    message: "{image} {starting, select, maybe {may not start} other {will not start}} with Secure Boot on, and this machine has Secure Boot on. Allow it for this run, or turn Secure Boot off in the machine's firmware first.",
  }),
  "deployment.nothingToEnd": msg({
    context: "deployment.nothingToEnd",
    message: "This machine has no run that is assigned or running. Load the page again to see its current state.",
  }),
  "deployment.rulesNoLongerChoose": msg({
    context: "deployment.rulesNoLongerChoose",
    message: "The rules no longer choose that sequence for this machine. {explanation} Look at the machine again.",
  }),
  "deployment.seedNamesMachine": msg({
    context: "deployment.seedNamesMachine",
    message: "The sequence's cloud-init seed gives the machine this name.",
  }),
  "deployment.sequenceGone": msg({
    context: "deployment.sequenceGone",
    message: "The sequence no longer exists. Load the page again and choose another sequence.",
  }),
  "deployment.sequenceHasProblems": msg({
    context: "deployment.sequenceHasProblems",
    message: "{count, plural, one {{sequence} has a problem, so it cannot run. Fix it on the sequence's page first.} other {{sequence} has # problems, so it cannot run. Fix them on the sequence's page first.}}",
  }),
  "deployment.settingsHaveProblems": msg({
    context: "deployment.settingsHaveProblems",
    message: "The deployment settings have problems, so no run starts until an administrator fixes them on the settings page: {problems}",
  }),
  "deployment.signerChooses": msg({
    context: "deployment.signerChooses",
    message: "{signer} signed in at the machine and chooses its sequence there. Approve it without a sequence.",
  }),
  "deployment.untrustedCaWithSecureBoot": msg({
    context: "deployment.untrustedCaWithSecureBoot",
    message: "{image} is signed under {ca}, which this machine's firmware does not trust, and this machine has Secure Boot on. Allow it for this run, or allow that CA or turn Secure Boot off in the machine's firmware first.",
  }),
  "directory.accountDisabled": msg({
    context: "directory.accountDisabled",
    message: "The DDT account {name} is disabled, so a sign-in is refused.",
  }),
  "directory.baseDnRefused": msg({
    context: "directory.baseDnRefused",
    message: "The directory at {server} refused the search under {baseDn}: {detail} Check the base DN on the Sign-in page.",
  }),
  "directory.bindAccountRefused": msg({
    context: "directory.bindAccountRefused",
    message: "The directory at {server} refused the bind account {bindDn}. Check the bind account and its password on the Sign-in page.",
  }),
  "directory.connectionIncomplete": msg({
    context: "directory.connectionIncomplete",
    message: "The directory connection is not complete. Enter the directory server and the base DN on the Sign-in page.",
  }),
  "directory.emptyMapNewAccount": msg({
    context: "directory.emptyMapNewAccount",
    message: "The directory's group map on the Sign-in page is empty, so administrators set roles. A first sign-in makes an account without one, which reaches nothing until an administrator gives it a role.",
  }),
  "directory.emptyMapNoRole": msg({
    context: "directory.emptyMapNoRole",
    message: "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has none yet, so it reaches nothing.",
  }),
  "directory.emptyMapRole": msg({
    context: "directory.emptyMapRole",
    message: "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has {role}.",
  }),
  "directory.enterUserName": msg({
    context: "directory.enterUserName",
    message: "Enter the user name to check.",
  }),
  "directory.inNoMappedGroup": msg({
    context: "directory.inNoMappedGroup",
    message: "{name} is in none of the groups the directory's group map on the Sign-in page gives a role, so a sign-in is refused.",
  }),
  "directory.localAccount": msg({
    context: "directory.localAccount",
    message: "{name} is a local account in DDT, so a sign-in with this name checks its DDT password and never asks the directory.",
  }),
  "directory.lockedOut": msg({
    context: "directory.lockedOut",
    message: "{reason} The account is locked out for now, so a sign-in waits until the lockout ends.",
  }),
  "directory.manyMatchingEntries": msg({
    context: "directory.manyMatchingEntries",
    message: "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.",
  }),
  "directory.noImmutableId": msg({
    context: "directory.noImmutableId",
    message: "The entry has no {attribute} value to key the account on, so a sign-in is refused.",
  }),
  "directory.noMatchingEntry": msg({
    context: "directory.noMatchingEntry",
    message: "No entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.",
  }),
  "directory.roleFromGroup": msg({
    context: "directory.roleFromGroup",
    message: "{name} gets {role} from {group}.",
  }),
  "directory.roleFromGroups": msg({
    context: "directory.roleFromGroups",
    message: "{name} is in {count} mapped groups and gets the highest role they give, {role} from {group}.",
  }),
  "directory.singleSignOnAccount": msg({
    context: "directory.singleSignOnAccount",
    message: "{name} is a single sign-on account in DDT, so a sign-in with this name and a password is refused.",
  }),
  "directory.turnedOff": msg({
    context: "directory.turnedOff",
    message: "Sign-in through a directory is off. Turn it on and set its connection on the Sign-in page first.",
  }),
  "directory.unreachable": msg({
    context: "directory.unreachable",
    message: "The directory at {server} could not be reached: {detail}",
  }),
  "domain.accountDisabled": msg({
    context: "domain.accountDisabled",
    message: "{user} is disabled.",
  }),
  "domain.accountExpired": msg({
    context: "domain.accountExpired",
    message: "{user} has expired.",
  }),
  "domain.checkError": msg({
    context: "domain.checkError",
    message: "{controller} answered the check with an error: {detail}",
  }),
  "domain.controllerOf": msg({
    context: "domain.controllerOf",
    message: "{controller} is a domain controller of {domain}.",
  }),
  "domain.controllerUnreachable": msg({
    context: "domain.controllerUnreachable",
    message: "{controller} could not be reached over LDAP. If this server's DNS does not know {domain}, enter a domain controller's name or address for the check on the Deployment defaults page. The machines find their domain controller through their own DNS.",
  }),
  "domain.controllerUnreachableWithDetail": msg({
    context: "domain.controllerUnreachableWithDetail",
    message: "{controller} could not be reached over LDAP ({detail}). If this server's DNS does not know {domain}, enter a domain controller's name or address for the check on the Deployment defaults page. The machines find their domain controller through their own DNS.",
  }),
  "domain.joinAccountUnknown": msg({
    context: "domain.joinAccountUnknown",
    message: "{controller} knows no account {user}. Correct the join account on the Deployment defaults page.",
  }),
  "domain.joinPasswordExpired": msg({
    context: "domain.joinPasswordExpired",
    message: "The password of {user} has expired. Give it a new one, in the domain and on the Deployment defaults page.",
  }),
  "domain.joinPasswordMustChange": msg({
    context: "domain.joinPasswordMustChange",
    message: "{user} has to change its password before it can sign in. Give it a new one, in the domain and on the Deployment defaults page.",
  }),
  "domain.joinPasswordRefused": msg({
    context: "domain.joinPasswordRefused",
    message: "{controller} did not accept the password of {user}. Correct the join account password on the Deployment defaults page.",
  }),
  "domain.lockedOut": msg({
    context: "domain.lockedOut",
    message: "{user} is locked out.",
  }),
  "domain.logonHours": msg({
    context: "domain.logonHours",
    message: "{user} may not sign in at this time of day (logon hours).",
  }),
  "domain.logonWorkstations": msg({
    context: "domain.logonWorkstations",
    message: "{user} may not sign in from the DDT server (Log On To workstations).",
  }),
  "domain.mayCreate": msg({
    context: "domain.mayCreate",
    message: "{user} may create computer objects in {container}, as many as it needs.",
  }),
  "domain.mayNotCreateInUnit": msg({
    context: "domain.mayNotCreateInUnit",
    message: "{user} may not create computer objects in {container}, and the machine account quota does not reach an organizational unit. Delegate \"Create Computer objects\" on it to the account, for example with the Delegation of Control wizard of Active Directory Users and Computers.",
  }),
  "domain.noComputersContainer": msg({
    context: "domain.noComputersContainer",
    message: "The default Computers container of {domain} was not found, or {user} may not read it.",
  }),
  "domain.noJoinAccountSet": msg({
    context: "domain.noJoinAccountSet",
    message: "The join account is not set. Set it and its password on the Deployment defaults page.",
  }),
  "domain.noSecureConnection": msg({
    context: "domain.noSecureConnection",
    message: "{controller} offers no LDAPS on port 636 that this server trusts, and on this operating system only LDAPS keeps the join account's password secret during the check. Give the domain controllers a certificate, for example from Active Directory Certificate Services, and trust its CA on this server. Joining does not depend on this check.",
  }),
  "domain.notSet": msg({
    context: "domain.notSet",
    message: "No domain is set. Set the domain and the join account on the Deployment defaults page.",
  }),
  "domain.organizationalUnitMissing": msg({
    context: "domain.organizationalUnitMissing",
    message: "{domain} has no organizational unit {organizationalUnit}, or {user} may not read it. Correct the organizational unit of the Join the domain step, or the one on the Deployment defaults page when the step names none.",
  }),
  "domain.quotaLeft": msg({
    context: "domain.quotaLeft",
    message: "{user} may not create computer objects in {container} by a right of its own, so it joins within the domain's machine account quota: {created} of {quota} used, {left} left. That also needs the user right \"Add workstations to domain\", which Authenticated Users hold by default and this check cannot read. Delegate \"Create Computer objects\" on the container to the account to join more.",
  }),
  "domain.quotaUnknown": msg({
    context: "domain.quotaUnknown",
    message: "{user} may not create computer objects in {container} by a right of its own, and the domain's machine account quota could not be read. Delegate \"Create Computer objects\" on the container to the account.",
  }),
  "domain.quotaUsed": msg({
    context: "domain.quotaUsed",
    message: "{user} may not create computer objects in {container} by a right of its own, and has used its machine account quota: {created} of {quota} (ms-DS-MachineAccountQuota). Delegate \"Create Computer objects\" on the container to the account.",
  }),
  "domain.servesOtherDomain": msg({
    context: "domain.servesOtherDomain",
    message: "{controller} serves the domain {namingContext}, not {domain} ({expected}). Correct the domain on the Deployment defaults page, or name a domain controller of that domain for the check there.",
  }),
  "domain.signInRefused": msg({
    context: "domain.signInRefused",
    message: "{controller} did not accept the user name or password of {user}.",
  }),
  "domain.signInRefusedWithReason": msg({
    context: "domain.signInRefusedWithReason",
    message: "{controller} did not accept the user name or password of {user} (reason {reason}).",
  }),
  "domain.signedIn": msg({
    context: "domain.signedIn",
    message: "Signed in to {controller} as {user} over {connection}.",
  }),
  "gpt.damaged": msg({
    context: "gpt.damaged",
    message: "The GUID partition table of the disk image is damaged.",
  }),
  "gpt.damagedEndsInTable": msg({
    context: "gpt.damagedEndsInTable",
    message: "The GUID partition table of the disk image is damaged. The file ends inside the partition table.",
  }),
  "gpt.damagedEntries": msg({
    context: "gpt.damagedEntries",
    message: "The GUID partition table of the disk image is damaged. Its partition entries are laid out in a way DDT does not read.",
  }),
  "gpt.damagedHeader": msg({
    context: "gpt.damagedHeader",
    message: "The GUID partition table of the disk image is damaged. Its header has a revision or size DDT does not know.",
  }),
  "gpt.damagedOverlap": msg({
    context: "gpt.damagedOverlap",
    message: "The GUID partition table of the disk image is damaged. Partitions {first} and {second} overlap.",
  }),
  "gpt.damagedPartitionOutside": msg({
    context: "gpt.damagedPartitionOutside",
    message: "The GUID partition table of the disk image is damaged. Partition {number} lies outside the usable sectors.",
  }),
  "gpt.damagedUsableRange": msg({
    context: "gpt.damagedUsableRange",
    message: "The GUID partition table of the disk image is damaged. Its usable range does not fit its own headers.",
  }),
  "gpt.fourKilobyteSectors": msg({
    context: "gpt.fourKilobyteSectors",
    message: "The disk image is made for disks with 4 KiB sectors. DDT writes images for disks with 512-byte sectors, which is what distributions publish.",
  }),
  "gpt.incomplete": msg({
    context: "gpt.incomplete",
    message: "The disk image holds {length} bytes, but its partitions reach to byte {end}. The file is incomplete.",
  }),
  "gpt.noTable": msg({
    context: "gpt.noTable",
    message: "The file has no GUID partition table, so it is not a UEFI disk image. Upload a disk image such as a distribution's cloud image.",
  }),
  "identity.concurrencyFailure": msg({
    context: "identity.concurrencyFailure",
    message: "Optimistic concurrency failure, object has been modified.",
  }),
  "identity.defaultError": msg({
    context: "identity.defaultError",
    message: "An unknown failure has occurred.",
  }),
  "identity.duplicateEmail": msg({
    context: "identity.duplicateEmail",
    message: "Email ''{email}'' is already taken.",
  }),
  "identity.duplicateRoleName": msg({
    context: "identity.duplicateRoleName",
    message: "Role name ''{role}'' is already taken.",
  }),
  "identity.duplicateUserName": msg({
    context: "identity.duplicateUserName",
    message: "Username ''{name}'' is already taken.",
  }),
  "identity.invalidEmail": msg({
    context: "identity.invalidEmail",
    message: "Email ''{email}'' is invalid.",
  }),
  "identity.invalidRoleName": msg({
    context: "identity.invalidRoleName",
    message: "Role name ''{role}'' is invalid.",
  }),
  "identity.invalidToken": msg({
    context: "identity.invalidToken",
    message: "Invalid token.",
  }),
  "identity.invalidUserName": msg({
    context: "identity.invalidUserName",
    message: "Username ''{name}'' is invalid, can only contain letters or digits.",
  }),
  "identity.loginAlreadyAssociated": msg({
    context: "identity.loginAlreadyAssociated",
    message: "A user with this login already exists.",
  }),
  "identity.other": msg({
    context: "identity.other",
    message: "{description}",
  }),
  "identity.passwordMismatch": msg({
    context: "identity.passwordMismatch",
    message: "Incorrect password.",
  }),
  "identity.passwordRequiresDigit": msg({
    context: "identity.passwordRequiresDigit",
    message: "Passwords must have at least one digit ('0'-'9').",
  }),
  "identity.passwordRequiresLower": msg({
    context: "identity.passwordRequiresLower",
    message: "Passwords must have at least one lowercase ('a'-'z').",
  }),
  "identity.passwordRequiresNonAlphanumeric": msg({
    context: "identity.passwordRequiresNonAlphanumeric",
    message: "Passwords must have at least one non alphanumeric character.",
  }),
  "identity.passwordRequiresUniqueChars": msg({
    context: "identity.passwordRequiresUniqueChars",
    message: "Passwords must use at least {count} different characters.",
  }),
  "identity.passwordRequiresUpper": msg({
    context: "identity.passwordRequiresUpper",
    message: "Passwords must have at least one uppercase ('A'-'Z').",
  }),
  "identity.passwordTooShort": msg({
    context: "identity.passwordTooShort",
    message: "Passwords must be at least {length} characters.",
  }),
  "identity.recoveryCodeRedemptionFailed": msg({
    context: "identity.recoveryCodeRedemptionFailed",
    message: "Recovery code redemption failed.",
  }),
  "identity.userAlreadyHasPassword": msg({
    context: "identity.userAlreadyHasPassword",
    message: "User already has a password set.",
  }),
  "identity.userAlreadyInRole": msg({
    context: "identity.userAlreadyInRole",
    message: "User already in role ''{role}''.",
  }),
  "identity.userLockoutNotEnabled": msg({
    context: "identity.userLockoutNotEnabled",
    message: "Lockout is not enabled for this user.",
  }),
  "identity.userNotInRole": msg({
    context: "identity.userNotInRole",
    message: "User is not in role ''{role}''.",
  }),
  "image.inUse": msg({
    context: "image.inUse",
    message: "Runs that are assigned or running use this image. Cancel them or let them finish, then delete it.",
  }),
  "image.otherArchitecture": msg({
    context: "image.otherArchitecture",
    message: "{image} is an {architecture} image, and DDT deploys only x64 Windows. Choose an x64 image.",
  }),
  "image.rawForOtherArchitecture": msg({
    context: "image.rawForOtherArchitecture",
    message: "{image} starts {architecture} machines, and DDT writes images for x64 machines. Choose an x64 image.",
  }),
  "image.withoutArchitecture": msg({
    context: "image.withoutArchitecture",
    message: "{image} does not say which processor it is for, and DDT deploys only x64 Windows. Choose an x64 image.",
  }),
  "mac.enterFull": msg({
    context: "mac.enterFull",
    message: "Enter a MAC address of 12 hex digits, such as 00:15:5D:01:02:03.",
  }),
  "mac.enterPart": msg({
    context: "mac.enterPart",
    message: "Enter 1 to 12 hex digits of a MAC address, such as 00:15:5D.",
  }),
  "machine.cannotBeRemoved": msg({
    context: "machine.cannotBeRemoved",
    message: "Only a rejected machine, or a waiting machine that was never approved and has no assigned run, can be removed.",
  }),
  "machine.changedWhileAssigning": msg({
    context: "machine.changedWhileAssigning",
    message: "The machine changed while the sequence was assigned. Look at it again before assigning.",
  }),
  "machine.changedWhileDeciding": msg({
    context: "machine.changedWhileDeciding",
    message: "The machine changed while this decision was made. Look at it again before deciding.",
  }),
  "machine.changedWhileRemoving": msg({
    context: "machine.changedWhileRemoving",
    message: "A machine changed while it was being removed. Look at it again before removing it.",
  }),
  "machine.inState": msg({
    context: "machine.inState",
    message: "The machine is {state}.",
  }),
  "machine.nobodySignedIn": msg({
    context: "machine.nobodySignedIn",
    message: "Nobody has signed in at this machine yet.",
  }),
  "machine.runChangedWhileStopping": msg({
    context: "machine.runChangedWhileStopping",
    message: "The run changed while it was being stopped. Look at the machine again.",
  }),
  "machineState.approved": msg({
    context: "machineState.approved",
    message: "Approved",
  }),
  "machineState.deploying": msg({
    context: "machineState.deploying",
    message: "Deploying",
  }),
  "machineState.done": msg({
    context: "machineState.done",
    message: "Done",
  }),
  "machineState.failed": msg({
    context: "machineState.failed",
    message: "Failed",
  }),
  "machineState.pending": msg({
    context: "machineState.pending",
    message: "Pending",
  }),
  "machineState.rejected": msg({
    context: "machineState.rejected",
    message: "Rejected",
  }),
  "machineState.retired": msg({
    context: "machineState.retired",
    message: "Retired",
  }),
  "model.enter": msg({
    context: "model.enter",
    message: "Enter the model as the machine reports it.",
  }),
  "model.manufacturerWhole": msg({
    context: "model.manufacturerWhole",
    message: "A manufacturer is matched whole, without {wildcard}.",
  }),
  "model.nameLength": msg({
    context: "model.nameLength",
    message: "A name here has at most {max} characters and no control characters.",
  }),
  "model.placeholder": msg({
    context: "model.placeholder",
    message: "{value} is what firmware reports when the field was never filled in. It says nothing about the machine.",
  }),
  "model.wildcardLast": msg({
    context: "model.wildcardLast",
    message: "Only the last character can be {wildcard}.",
  }),
  "model.wildcardPrefix": msg({
    context: "model.wildcardPrefix",
    message: "Put at least {min} characters before {wildcard}, so it matches only one family of models.",
  }),
  "organizationalUnit.isComputers": msg({
    context: "organizationalUnit.isComputers",
    message: "The default Computers container is no organizational unit and cannot be named. Leave this empty to use it.",
  }),
  "organizationalUnit.notDistinguished": msg({
    context: "organizationalUnit.notDistinguished",
    message: "''{value}'' is not a distinguished name. Write it like OU=Workstations,DC=example,DC=com.",
  }),
  "organizationalUnit.withPrefix": msg({
    context: "organizationalUnit.withPrefix",
    message: "Must be a distinguished name without the LDAP:// prefix, such as OU=Workstations,DC=example,DC=com.",
  }),
  "package.bootImageDriversOnly": msg({
    context: "package.bootImageDriversOnly",
    message: "Only a driver package can go into the Windows PE boot image.",
  }),
  "package.entryAtRoot": msg({
    context: "package.entryAtRoot",
    message: "The entry {entry} starts at the root of a drive.",
  }),
  "package.entryChecksum": msg({
    context: "package.entryChecksum",
    message: "The entry {entry} does not unpack to the bytes the zip says it holds. Create the zip again.",
  }),
  "package.entryCompression": msg({
    context: "package.entryCompression",
    message: "The entry {entry} is compressed in a way DDT cannot unpack. Create the zip with Deflate.",
  }),
  "package.entryControlCharacter": msg({
    context: "package.entryControlCharacter",
    message: "The entry {entry} has a control character in its name.",
  }),
  "package.entryDamaged": msg({
    context: "package.entryDamaged",
    message: "The entry {entry} is damaged. Create the zip again.",
  }),
  "package.entryDeviceName": msg({
    context: "package.entryDeviceName",
    message: "The entry {entry} has a name Windows keeps for a device, such as CON or NUL.",
  }),
  "package.entryEmptyName": msg({
    context: "package.entryEmptyName",
    message: "The entry {entry} has an empty name, or a folder name that points out of the package.",
  }),
  "package.entryEncrypted": msg({
    context: "package.entryEncrypted",
    message: "The entry {entry} is encrypted. Upload a zip without a password.",
  }),
  "package.entryForbiddenCharacter": msg({
    context: "package.entryForbiddenCharacter",
    message: "The entry {entry} has a character Windows does not allow in names, such as : for a drive or a data stream.",
  }),
  "package.entryNameTooLong": msg({
    context: "package.entryNameTooLong",
    message: "The entry {entry} has a name longer than {max} characters.",
  }),
  "package.entrySize": msg({
    context: "package.entrySize",
    message: "The entry {entry} unpacks to {actual} bytes, but the zip says {declared}. Create the zip again.",
  }),
  "package.entrySymbolicLink": msg({
    context: "package.entrySymbolicLink",
    message: "The entry {entry} is a symbolic link. Put the file itself in the zip.",
  }),
  "package.entryTooDeep": msg({
    context: "package.entryTooDeep",
    message: "The entry {entry} is more than {max} folders deep.",
  }),
  "package.entryTrailingDot": msg({
    context: "package.entryTrailingDot",
    message: "The entry {entry} has a name that ends in a dot or a space, which Windows cannot create.",
  }),
  "package.entryTwice": msg({
    context: "package.entryTwice",
    message: "The entry {entry} is in the zip twice, if case is ignored as Windows ignores it.",
  }),
  "package.fileAndFolder": msg({
    context: "package.fileAndFolder",
    message: "The zip has a file {entry} and a folder of the same name.",
  }),
  "package.fileMissing": msg({
    context: "package.fileMissing",
    message: "The package's file is missing from the server's library. Upload it again.",
  }),
  "package.filesHaveNoTargets": msg({
    context: "package.filesHaveNoTargets",
    message: "A Files package is unpacked for the Run script steps that name it, not by the machine's model, so it has no targets.",
  }),
  "package.inUse": msg({
    context: "package.inUse",
    message: "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.",
  }),
  "package.noInf": msg({
    context: "package.noInf",
    message: "A driver package needs at least one .inf file. Zip the folder that holds the drivers' .inf files.",
  }),
  "package.notZip": msg({
    context: "package.notZip",
    message: "The file is not a zip archive, or it is damaged. Upload a zip file.",
  }),
  "package.targetEmpty": msg({
    context: "package.targetEmpty",
    message: "A target is empty.",
  }),
  "package.targetTwice": msg({
    context: "package.targetTwice",
    message: "{model} is a target twice.",
  }),
  "package.targetsMissing": msg({
    context: "package.targetsMissing",
    message: "Send the list of targets, empty for none.",
  }),
  "package.tooLargeUnpacked": msg({
    context: "package.tooLargeUnpacked",
    message: "Unpacked, the zip would take more than {max} GB.",
  }),
  "package.tooManyEntries": msg({
    context: "package.tooManyEntries",
    message: "The zip holds {count} entries. A package can hold at most {max}.",
  }),
  "package.tooManyTargets": msg({
    context: "package.tooManyTargets",
    message: "A package can have at most {max} targets.",
  }),
  "person.someOperator": msg({
    context: "person.someOperator",
    message: "An operator",
  }),
  "resolution.assignedOnWeb": msg({
    context: "resolution.assignedOnWeb",
    message: "{by} assigned {sequence} on the web, which comes before every rule.",
  }),
  "resolution.cannotRun": msg({
    context: "resolution.cannotRun",
    message: "{explanation} {count, plural, one {{sequence} has # problem, so it cannot run until it is fixed.} other {{sequence} has # problems, so it cannot run until they are fixed.}}",
  }),
  "resolution.chosenAtMachine": msg({
    context: "resolution.chosenAtMachine",
    message: "{by} chose {sequence} at the machine, which comes before every rule.",
  }),
  "resolution.noRule": msg({
    context: "resolution.noRule",
    message: "No rule matches the MAC addresses or the model of this machine, so an operator chooses its sequence.",
  }),
  "resolution.ruleChooses": msg({
    context: "resolution.ruleChooses",
    message: "The rule for {rule} chooses {sequence}. A rule only chooses: the machine still needs an approval on the web, or someone who signs in at it, where the sequence is offered.",
  }),
  "role.aViewer": msg({
    context: "role.aViewer",
    message: "a Viewer",
  }),
  "role.administrator": msg({
    context: "role.administrator",
    message: "Administrator",
  }),
  "role.anAdministrator": msg({
    context: "role.anAdministrator",
    message: "an Administrator",
  }),
  "role.anOperator": msg({
    context: "role.anOperator",
    message: "an Operator",
  }),
  "role.operator": msg({
    context: "role.operator",
    message: "Operator",
  }),
  "role.viewer": msg({
    context: "role.viewer",
    message: "Viewer",
  }),
  "rule.chooseKind": msg({
    context: "rule.chooseKind",
    message: "Choose a rule by MAC address or by model.",
  }),
  "rule.exists": msg({
    context: "rule.exists",
    message: "There is a rule for {rule} already. It chooses {sequence}; change that rule instead.",
  }),
  "rule.forMac": msg({
    context: "rule.forMac",
    message: "MAC address {mac}",
  }),
  "rule.forModel": msg({
    context: "rule.forModel",
    message: "model {manufacturer} {model}",
  }),
  "rule.forModelOfAnyMaker": msg({
    context: "rule.forModelOfAnyMaker",
    message: "model {model} of any maker",
  }),
  "rule.sequenceGone": msg({
    context: "rule.sequenceGone",
    message: "The sequence no longer exists. Choose another one.",
  }),
  "sequence.cannotGoOn": msg({
    context: "sequence.cannotGoOn",
    message: "The sequence cannot go on when {activity, select, partition {partitioning the disk} image {applying the image} other {writing the raw disk image}} fails.",
  }),
  "sequence.cannotSkip": msg({
    context: "sequence.cannotSkip",
    message: "The sequence cannot skip {activity, select, partition {partitioning the disk} image {applying the image} other {writing the raw disk image}}, so this step cannot have conditions.",
  }),
  "sequence.changedWhileDeleting": msg({
    context: "sequence.changedWhileDeleting",
    message: "The sequence changed, or a rule chose it, while it was being deleted. Look at it again before deleting it.",
  }),
  "sequence.chooseImage": msg({
    context: "sequence.chooseImage",
    message: "Choose the image to apply.",
  }),
  "sequence.chooseRawImage": msg({
    context: "sequence.chooseRawImage",
    message: "Choose the raw disk image to write.",
  }),
  "sequence.chosenByRules": msg({
    context: "sequence.chosenByRules",
    message: "{count, plural, one {A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.} other {# rules choose this sequence. Delete them or let them choose another sequence, then delete this one.}}",
  }),
  "sequence.conditionEmpty": msg({
    context: "sequence.conditionEmpty",
    message: "The condition is empty.",
  }),
  "sequence.conditionOperator": msg({
    context: "sequence.conditionOperator",
    message: "Choose an operator.",
  }),
  "sequence.conditionValue": msg({
    context: "sequence.conditionValue",
    message: "Enter the value to compare with.",
  }),
  "sequence.conditionVariable": msg({
    context: "sequence.conditionVariable",
    message: "Choose one of the variables {variables}.",
  }),
  "sequence.conditionsMissing": msg({
    context: "sequence.conditionsMissing",
    message: "The conditions are missing.",
  }),
  "sequence.driversBeforeImage": msg({
    context: "sequence.driversBeforeImage",
    message: "Drivers can be added only after the image is applied.",
  }),
  "sequence.exitCodeMeansBoth": msg({
    context: "sequence.exitCodeMeansBoth",
    message: "An exit code cannot mean both success and a restart: {codes}.",
  }),
  "sequence.imageBeforePartition": msg({
    context: "sequence.imageBeforePartition",
    message: "The image can be applied only after a step that partitions the disk.",
  }),
  "sequence.imageGone": msg({
    context: "sequence.imageGone",
    message: "The image is no longer in the library. Choose another image.",
  }),
  "sequence.imageIsRaw": msg({
    context: "sequence.imageIsRaw",
    message: "{image} is a raw disk image, which a Write raw disk image step writes. Choose a Windows image.",
  }),
  "sequence.imageIsWindows": msg({
    context: "sequence.imageIsWindows",
    message: "{image} is a Windows image, which an Apply image step applies. Choose a raw disk image.",
  }),
  "sequence.keyboard": msg({
    context: "sequence.keyboard",
    message: "''{keyboard}'' is not an input locale. Use a name such as de-DE or a code such as 0407:00000407.",
  }),
  "sequence.locale": msg({
    context: "sequence.locale",
    message: "''{locale}'' is not a language and region that Windows knows. Use a name such as de-DE.",
  }),
  "sequence.nameTaken": msg({
    context: "sequence.nameTaken",
    message: "Another sequence is already called {name}. Choose another name.",
  }),
  "sequence.noAdministratorWarning": msg({
    context: "sequence.noAdministratorWarning",
    message: "The sequence continues in Windows, but no Write answer file step adds the local administrator. Windows setup then stops at the account page, and the sequence waits there until someone finishes it.",
  }),
  "sequence.noDomainSet": msg({
    context: "sequence.noDomainSet",
    message: "No domain is set on the Deployment defaults page, so the machine has no domain to join. Set one there or remove this step.",
  }),
  "sequence.noLocalAdministratorSet": msg({
    context: "sequence.noLocalAdministratorSet",
    message: "No local administrator is set on the Deployment defaults page, so the answer file cannot add one.",
  }),
  "sequence.oneDomainJoin": msg({
    context: "sequence.oneDomainJoin",
    message: "A sequence can join the domain only once.",
  }),
  "sequence.oneImage": msg({
    context: "sequence.oneImage",
    message: "A sequence can apply only one image.",
  }),
  "sequence.onePartition": msg({
    context: "sequence.onePartition",
    message: "A sequence can partition the disk only once.",
  }),
  "sequence.oneRawImage": msg({
    context: "sequence.oneRawImage",
    message: "A sequence can write only one raw disk image.",
  }),
  "sequence.oneSeed": msg({
    context: "sequence.oneSeed",
    message: "A sequence can write the cloud-init seed only once.",
  }),
  "sequence.oneUnattend": msg({
    context: "sequence.oneUnattend",
    message: "A sequence can write the answer file only once.",
  }),
  "sequence.packageBeforePartition": msg({
    context: "sequence.packageBeforePartition",
    message: "In Windows PE, a script with a package runs only after the disk is partitioned, where the package is put.",
  }),
  "sequence.packageGone": msg({
    context: "sequence.packageGone",
    message: "The package is no longer in the library. Choose another package, or none.",
  }),
  "sequence.packageIsDrivers": msg({
    context: "sequence.packageIsDrivers",
    message: "{package} is a driver package. A script runs with a files package.",
  }),
  "sequence.packageWithRawImage": msg({
    context: "sequence.packageWithRawImage",
    message: "A sequence that writes a raw disk image has no partition to unpack a package on, so its scripts cannot have one.",
  }),
  "sequence.partitionAfterImage": msg({
    context: "sequence.partitionAfterImage",
    message: "The disk has to be partitioned before the image is applied.",
  }),
  "sequence.rawImageNotStarting": msg({
    context: "sequence.rawImageNotStarting",
    message: "{image} {starting, select, maybe {may not start} other {will not start}} with Secure Boot on. {detail} Turn Secure Boot off in the firmware of the machines it goes to, or enroll your own key. Assigning the sequence then asks to allow it.",
  }),
  "sequence.rebootExitCodes": msg({
    context: "sequence.rebootExitCodes",
    message: "Enter at most {max} exit codes that ask for a restart.",
  }),
  "sequence.recoveryPartitionSize": msg({
    context: "sequence.recoveryPartitionSize",
    message: "The recovery partition needs {min} to {max} MB.",
  }),
  "sequence.requestTooLarge": msg({
    context: "sequence.requestTooLarge",
    message: "A sequence request can have at most {max} KiB.",
  }),
  "sequence.restartAfterRestart": msg({
    context: "sequence.restartAfterRestart",
    message: "A restart step restarts anyway, so it cannot restart again after it.",
  }),
  "sequence.restartBeforePartition": msg({
    context: "sequence.restartBeforePartition",
    message: "Windows PE can restart only after the disk is partitioned, because the run's state is kept on the disk.",
  }),
  "sequence.restartWithRawImage": msg({
    context: "sequence.restartWithRawImage",
    message: "A sequence that writes a raw disk image keeps its state in memory, so Windows PE cannot restart during it.",
  }),
  "sequence.scriptEmpty": msg({
    context: "sequence.scriptEmpty",
    message: "Enter the script.",
  }),
  "sequence.scriptInterpreter": msg({
    context: "sequence.scriptInterpreter",
    message: "Choose cmd or PowerShell.",
  }),
  "sequence.scriptPhase": msg({
    context: "sequence.scriptPhase",
    message: "Choose Windows PE or Windows.",
  }),
  "sequence.scriptTimeout": msg({
    context: "sequence.scriptTimeout",
    message: "The timeout has to be 1 to {max} minutes.",
  }),
  "sequence.scriptTooLarge": msg({
    context: "sequence.scriptTooLarge",
    message: "A script can have at most {max} KiB.",
  }),
  "sequence.seedBeforeRawImage": msg({
    context: "sequence.seedBeforeRawImage",
    message: "The cloud-init seed can be written only after a step that writes a raw disk image.",
  }),
  "sequence.seedFileMissing": msg({
    context: "sequence.seedFileMissing",
    message: "The {file} file is missing. It can be empty.",
  }),
  "sequence.seedFileTooLarge": msg({
    context: "sequence.seedFileTooLarge",
    message: "The {file} file can have at most {max} KiB.",
  }),
  "sequence.sendJson": msg({
    context: "sequence.sendJson",
    message: "Send the sequence as JSON.",
  }),
  "sequence.stepCount": msg({
    context: "sequence.stepCount",
    message: "A sequence needs 1 to {max} steps.",
  }),
  "sequence.stepEmpty": msg({
    context: "sequence.stepEmpty",
    message: "A step is empty.",
  }),
  "sequence.stepIdRepeated": msg({
    context: "sequence.stepIdRepeated",
    message: "Step {number} has the id of an earlier step.",
  }),
  "sequence.stepNameEmpty": msg({
    context: "sequence.stepNameEmpty",
    message: "Enter a name for the step.",
  }),
  "sequence.stepNameTooLong": msg({
    context: "sequence.stepNameTooLong",
    message: "A step name can have at most {max} characters.",
  }),
  "sequence.stepWithoutId": msg({
    context: "sequence.stepWithoutId",
    message: "Step {number} has no id.",
  }),
  "sequence.successExitCodes": msg({
    context: "sequence.successExitCodes",
    message: "Enter 1 to {max} exit codes that mean success.",
  }),
  "sequence.systemPartitionSize": msg({
    context: "sequence.systemPartitionSize",
    message: "The system partition needs {min} to {max} MB.",
  }),
  "sequence.timeZone": msg({
    context: "sequence.timeZone",
    message: "''{timeZone}'' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time.",
  }),
  "sequence.tooManyConditions": msg({
    context: "sequence.tooManyConditions",
    message: "A step can have at most {max} conditions.",
  }),
  "sequence.unattendBeforeImage": msg({
    context: "sequence.unattendBeforeImage",
    message: "The answer file can be written only after the image is applied.",
  }),
  "sequence.unknownPlaceholders": msg({
    context: "sequence.unknownPlaceholders",
    message: "{count, plural, one {{named} is not one of DDT's placeholders, so it stays as it is. DDT fills in {known}.} other {{named} are not DDT's placeholders, so they stay as they are. DDT fills in {known}.}}",
  }),
  "sequence.unknownStep": msg({
    context: "sequence.unknownStep",
    message: "This version of DDT does not know this kind of step.",
  }),
  "sequence.unreadable": msg({
    context: "sequence.unreadable",
    message: "The sequence is not a document DDT can read. Every step needs an id and a kind this version of DDT knows.",
  }),
  "sequence.versionTooLow": msg({
    context: "sequence.versionTooLow",
    message: "The sequence has steps of version {required}, but says it is of version {version}.",
  }),
  "sequence.versionUnsupported": msg({
    context: "sequence.versionUnsupported",
    message: "This version of DDT runs sequences of version 1 to {current}, not {version}.",
  }),
  "sequence.windowsNeedsImage": msg({
    context: "sequence.windowsNeedsImage",
    message: "A step in Windows needs an earlier step that applies the image without conditions.",
  }),
  "sequence.windowsPEAfterWindows": msg({
    context: "sequence.windowsPEAfterWindows",
    message: "Steps in Windows PE come first, and an earlier step already runs in Windows.",
  }),
  "sequence.windowsWithRawImage": msg({
    context: "sequence.windowsWithRawImage",
    message: "A sequence either installs Windows or writes a raw disk image. This step belongs to installing Windows.",
  }),
  "settings.agent.configured": msg({
    context: "settings.agent.configured",
    message: "DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.",
  }),
  "settings.agent.notExecutable": msg({
    context: "settings.agent.notExecutable",
    message: "That is not a Windows executable. Upload ddt-agent.exe as Publish-Agent.ps1 builds it.",
  }),
  "settings.agent.tooLarge": msg({
    context: "settings.agent.tooLarge",
    message: "The agent may be at most {max} MB.",
  }),
  "settings.apply.oidcClosed": msg({
    context: "settings.apply.oidcClosed",
    message: "Single sign-on is off on this host while the section has problems: {problems}",
  }),
  "settings.apply.oidcFailed": msg({
    context: "settings.apply.oidcFailed",
    message: "Single sign-on is off on this host: {error}",
  }),
  "settings.apply.proxiesClosed": msg({
    context: "settings.apply.proxiesClosed",
    message: "No proxy is trusted on this host while the section has problems: {problems}",
  }),
  "settings.apply.pxeBindFailed": msg({
    context: "settings.apply.pxeBindFailed",
    message: "DDT could not bind UDP {port} for {protocol, select, proxyDhcp {ProxyDHCP} bootServer {PXE boot server} tftp {TFTP} tftpSinglePort {TFTP (single port)} other {{protocol}}} ({error}). {error, select, AccessDenied {The process may not bind a privileged port. Grant NET_BIND_SERVICE, as build/compose.yaml does.} AddressAlreadyInUse {Another DHCP, PXE or TFTP service already holds this port on this host. Stop it, or run DDT without the pxe role here.} other {Check that no other service holds the port.}}",
  }),
  "settings.apply.pxeClosed": msg({
    context: "settings.apply.pxeClosed",
    message: "The pxe settings have problems, so nothing is served until they are fixed: {problems}",
  }),
  "settings.atLeastOne": msg({
    context: "settings.atLeastOne",
    message: "Must be at least 1.",
  }),
  "settings.certificate.addHostFirst": msg({
    context: "settings.certificate.addHostFirst",
    message: "Add {host}, the name this page is reached by, to the server names first.",
  }),
  "settings.certificate.generateOff": msg({
    context: "settings.certificate.generateOff",
    message: "DDT:Https:GenerateSelfSignedCertificate is false, so DDT issues no certificate. Upload one instead.",
  }),
  "settings.certificate.keyAlgorithm": msg({
    context: "settings.certificate.keyAlgorithm",
    message: "Its key is neither RSA nor ECDSA.",
  }),
  "settings.certificate.keyMismatch": msg({
    context: "settings.certificate.keyMismatch",
    message: "The certificate does not load with this key: {error}",
  }),
  "settings.certificate.missingNames": msg({
    context: "settings.certificate.missingNames",
    message: "It does not name {names}, which the server is reached by, so browsers and agents would refuse it.",
  }),
  "settings.certificate.nameInvalid": msg({
    context: "settings.certificate.nameInvalid",
    message: "''{name}'' is not a host name or an address. Write each name alone, such as ddt.corp.example or 10.0.0.5.",
  }),
  "settings.certificate.newRootGenerate": msg({
    context: "settings.certificate.newRootGenerate",
    message: "DDT has no root yet, so Generate makes one: build every boot image again with it, and trust it in the browsers that manage DDT.",
  }),
  "settings.certificate.newRootUpload": msg({
    context: "settings.certificate.newRootUpload",
    message: "This certificate does not come from DDT's root, which every boot image pins: build every boot image again with its root, and trust that root in the browsers that manage DDT.",
  }),
  "settings.certificate.notBase64": msg({
    context: "settings.certificate.notBase64",
    message: "Is not base64.",
  }),
  "settings.certificate.notManageable": msg({
    context: "settings.certificate.notManageable",
    message: "The page manages the certificate only when Kestrel:Certificates:Default:Path and KeyPath both name PEM files and no Password is set. A PFX, a key under a password, or TLS at a proxy is managed by hand.",
  }),
  "settings.certificate.notServedNew": msg({
    context: "settings.certificate.notServedNew",
    message: "This connection was served the certificate before the new one, so it proves nothing about the new one. Load the page again, which connects anew, and confirm from there.",
  }),
  "settings.certificate.notValidNow": msg({
    context: "settings.certificate.notValidNow",
    message: "It is valid from {from} to {until}, not now.",
  }),
  "settings.certificate.nothingToConfirm": msg({
    context: "settings.certificate.nothingToConfirm",
    message: "No certificate waits for a confirmation.",
  }),
  "settings.certificate.pfxPassword": msg({
    context: "settings.certificate.pfxPassword",
    message: "Does not open with this password: {error}",
  }),
  "settings.certificate.pfxWithoutKey": msg({
    context: "settings.certificate.pfxWithoutKey",
    message: "Holds no certificate with its private key.",
  }),
  "settings.certificate.sendPair": msg({
    context: "settings.certificate.sendPair",
    message: "Send the certificate and its key, or a PFX.",
  }),
  "settings.certificate.sendPem": msg({
    context: "settings.certificate.sendPem",
    message: "Send the certificate with its intermediates and its key as PEM, or a PFX.",
  }),
  "settings.configurationUnreadable": msg({
    context: "settings.configurationUnreadable",
    message: "{section} cannot be read from configuration: {error}",
  }),
  "settings.console.configured": msg({
    context: "settings.console.configured",
    message: "DDT:Agent:ConsolePath names the console in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.",
  }),
  "settings.console.notAPackage": msg({
    context: "settings.console.notAPackage",
    message: "That is not the console. Upload a zip of the folder Publish-Console.ps1 writes, with ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll and nothing else.",
  }),
  "settings.console.tooLarge": msg({
    context: "settings.console.tooLarge",
    message: "The console may be at most {max} MB, zipped and unpacked.",
  }),
  "settings.deployment.administratorNameInvalid": msg({
    context: "settings.deployment.administratorNameInvalid",
    message: "''{value}'' is not a valid account name. Use 1 to {max} characters and none of \" / \\ [ ] : ; | = , + * ? < >.",
  }),
  "settings.deployment.administratorPasswordRequired": msg({
    context: "settings.deployment.administratorPasswordRequired",
    message: "Required when Domain:Name is set. Without a local administrator, a domain machine stops at the account page of its first start.",
  }),
  "settings.deployment.consoleLanguageUnknown": msg({
    context: "settings.deployment.consoleLanguageUnknown",
    message: "''{value}'' is not a language the console at the machine speaks. Use en or de, or leave it empty for the language of Windows PE.",
  }),
  "settings.deployment.controllerInvalid": msg({
    context: "settings.deployment.controllerInvalid",
    message: "''{value}'' is not a host name or an address. Name the domain controller alone, such as dc1.corp.example or 10.0.0.10, without a scheme or a port.",
  }),
  "settings.deployment.domainUserNameForm": msg({
    context: "settings.deployment.domainUserNameForm",
    message: "''{value}'' must be written as DOMAIN\\user or user@domain.example.",
  }),
  "settings.deployment.domainUserNameRequired": msg({
    context: "settings.deployment.domainUserNameRequired",
    message: "Required when Domain:Name is set. Name the account that joins the machines, as DOMAIN\\user or user@domain.example.",
  }),
  "settings.deployment.localeUnknown": msg({
    context: "settings.deployment.localeUnknown",
    message: "''{value}'' is not a culture name. Use one such as de-DE or en-US, or leave it empty for the image's own language.",
  }),
  "settings.deployment.requiredWithDomain": msg({
    context: "settings.deployment.requiredWithDomain",
    message: "Required when Domain:Name is set.",
  }),
  "settings.deployment.timeZoneUnknown": msg({
    context: "settings.deployment.timeZoneUnknown",
    message: "''{value}'' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time, or leave it empty so that Windows picks the zone of the locale.",
  }),
  "settings.enterPasswordAgain": msg({
    context: "settings.enterPasswordAgain",
    message: "Enter your password again to change {fields}.",
  }),
  "settings.fieldProblem": msg({
    context: "settings.fieldProblem",
    message: "{field}: {problem}",
  }),
  "settings.keyRingUnreadable": msg({
    context: "settings.keyRingUnreadable",
    message: "This server cannot read the key ring the stored settings secrets were encrypted with, so it saves no settings. Every DDT process on one database has to share the key ring in DDT:StorePath/keys.",
  }),
  "settings.ldap.hostRequired": msg({
    context: "settings.ldap.hostRequired",
    message: "Required while directory sign-in is on. Name the directory server, such as dc1.corp.example.",
  }),
  "settings.ldap.nestedGroupsOff": msg({
    context: "settings.ldap.nestedGroupsOff",
    message: "false reads no groups, so every directory user would be refused. Turn it on, or empty GroupRoleMap.",
  }),
  "settings.ldap.noAdministrator": msg({
    context: "settings.ldap.noAdministrator",
    message: "No group maps to Administrator, so every directory account that is an administrator loses the role at its next sign-in.",
  }),
  "settings.ldap.portInvalid": msg({
    context: "settings.ldap.portInvalid",
    message: "{port} is not a port number. LDAPS uses 636, and StartTLS 389.",
  }),
  "settings.ldap.rekey": msg({
    context: "settings.ldap.rekey",
    message: "Every directory account is keyed on {current}. With {next} each one is taken for a new person at its next sign-in, and its roles and history stay with the old account.",
  }),
  "settings.ldap.testOwnSignIn": msg({
    context: "settings.ldap.testOwnSignIn",
    message: "You sign in through the directory, and these values decide whether you still can. Test your own sign-in with them first; the save is accepted while a test that kept you an administrator is less than 5 minutes old.",
  }),
  "settings.ldap.timeoutNotPositive": msg({
    context: "settings.ldap.timeoutNotPositive",
    message: "Must be longer than zero, such as 00:00:10.",
  }),
  "settings.ldap.transportInvalid": msg({
    context: "settings.ldap.transportInvalid",
    message: "Must be Ldaps, StartTls or UnencryptedDangerous.",
  }),
  "settings.ldap.unencrypted": msg({
    context: "settings.ldap.unencrypted",
    message: "The bind password and every password typed at sign-in cross the network in clear text.",
  }),
  "settings.ldap.userFilterBraces": msg({
    context: "settings.ldap.userFilterBraces",
    message: "Is not a valid template: a brace that is not part of {placeholder} has to be written twice, as {open} or {close}.",
  }),
  "settings.ldap.userFilterPlaceholder": msg({
    context: "settings.ldap.userFilterPlaceholder",
    message: "Must contain {placeholder}, which DDT replaces with the user name, such as (&(objectClass=user)(sAMAccountName={placeholder})).",
  }),
  "settings.ldapTest.accountDisabled": msg({
    context: "settings.ldapTest.accountDisabled",
    message: "{name} is disabled, so a sign-in is refused before the directory is asked.",
  }),
  "settings.ldapTest.bindFailed": msg({
    context: "settings.ldapTest.bindFailed",
    message: "The bind as {account} to {server} failed: {error}",
  }),
  "settings.ldapTest.bound": msg({
    context: "settings.ldapTest.bound",
    message: "The bind as {account} to {server} succeeded.",
  }),
  "settings.ldapTest.found": msg({
    context: "settings.ldapTest.found",
    message: "{entry} was found, in {count, plural, one {# group} other {# groups}}.",
  }),
  "settings.ldapTest.lockedOut": msg({
    context: "settings.ldapTest.lockedOut",
    message: "{name} is locked out, so a sign-in is refused before the directory is asked.",
  }),
  "settings.ldapTest.manyEntries": msg({
    context: "settings.ldapTest.manyEntries",
    message: "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in is refused.",
  }),
  "settings.ldapTest.noEntry": msg({
    context: "settings.ldapTest.noEntry",
    message: "No entry under {baseDn} matches {name} with the user filter.",
  }),
  "settings.ldapTest.noImmutableId": msg({
    context: "settings.ldapTest.noImmutableId",
    message: "{entry} has no {attribute}, so a sign-in is refused.",
  }),
  "settings.ldapTest.noRole": msg({
    context: "settings.ldapTest.noRole",
    message: "{result} The group map gives no role, so a sign-in is refused.",
  }),
  "settings.ldapTest.notDirectoryAccount": msg({
    context: "settings.ldapTest.notDirectoryAccount",
    message: "{name} is not a directory account, so its password is not sent to the directory.",
  }),
  "settings.ldapTest.passwordRefused": msg({
    context: "settings.ldapTest.passwordRefused",
    message: "{entry} was found, but the directory refused the password.",
  }),
  "settings.ldapTest.role": msg({
    context: "settings.ldapTest.role",
    message: "{result} The group map makes the account {role}.",
  }),
  "settings.ldapTest.searchFailed": msg({
    context: "settings.ldapTest.searchFailed",
    message: "The search for {name} under {baseDn} failed: {error}",
  }),
  "settings.ldapTest.sendValues": msg({
    context: "settings.ldapTest.sendValues",
    message: "Send the values to test.",
  }),
  "settings.logging.levelUnknown": msg({
    context: "settings.logging.levelUnknown",
    message: "''{value}'' is not a log level. Use Trace, Debug, Information, Warning, Error, Critical or None.",
  }),
  "settings.machines.networkEverything": msg({
    context: "settings.machines.networkEverything",
    message: "''{value}'' is every address there is. Name the provisioning networks themselves.",
  }),
  "settings.machines.networkHoldsProxy": msg({
    context: "settings.machines.networkHoldsProxy",
    message: "The zero touch network {network} contains the proxy {proxy}. A request the proxy forwards without the client's address would count as one from that network.",
  }),
  "settings.machines.networkInvalid": msg({
    context: "settings.machines.networkInvalid",
    message: "''{value}'' is not a network. Write each one as an address and a prefix length with no address bits set after the prefix, such as 10.20.0.0/16, fd00:20::/64 or 10.20.1.5/32 for one machine.",
  }),
  "settings.machines.networkOverlapsProxies": msg({
    context: "settings.machines.networkOverlapsProxies",
    message: "The zero touch network {network} overlaps the proxy network {proxies}. A request a proxy there forwards without the client's address would count as one from that network.",
  }),
  "settings.machines.networkWide": msg({
    context: "settings.machines.networkWide",
    message: "{network} is wider than a /{prefix}. Every address in it counts as a zero touch address.",
  }),
  "settings.machines.perAddressRange": msg({
    context: "settings.machines.perAddressRange",
    message: "Must be between 1 and MaxWaiting, which is {max}.",
  }),
  "settings.noLocalAdministrator": msg({
    context: "settings.noLocalAdministrator",
    message: "No local administrator account is enabled. Should the directory or the provider stop granting the Administrator role, only the console command settings create-admin could let anyone in again.",
  }),
  "settings.noSuchSecret": msg({
    context: "settings.noSuchSecret",
    message: "{section} has no secret called {name}.",
  }),
  "settings.oidc.authorityRequired": msg({
    context: "settings.oidc.authorityRequired",
    message: "Required while single sign-on is on: the provider's https address, such as https://login.example.com/realms/ddt.",
  }),
  "settings.oidc.cannotStart": msg({
    context: "settings.oidc.cannotStart",
    message: "Single sign-on cannot start with these values: {error}",
  }),
  "settings.oidc.clientIdRequired": msg({
    context: "settings.oidc.clientIdRequired",
    message: "Required while single sign-on is on: the client id the provider shows for DDT.",
  }),
  "settings.oidc.groupsClaimRequired": msg({
    context: "settings.oidc.groupsClaimRequired",
    message: "GroupRoleMap needs the claim that carries the groups, such as groups.",
  }),
  "settings.oidc.operatorRole": msg({
    context: "settings.oidc.operatorRole",
    message: "Every identity the provider signs in that DDT has not seen becomes an operator, and operators can read the deployment passwords by deploying a machine they control.",
  }),
  "settings.oidc.provisionAdministrator": msg({
    context: "settings.oidc.provisionAdministrator",
    message: "{administrator} would make every identity the provider signs in that DDT has not seen an administrator. Use {viewer} or {operator}, or map a group to {administrator} in GroupRoleMap.",
  }),
  "settings.oidc.provisionRoleUnknown": msg({
    context: "settings.oidc.provisionRoleUnknown",
    message: "''{role}'' is not a DDT role. Use {viewer} or {operator}.",
  }),
  "settings.oidc.scopesWithoutOpenid": msg({
    context: "settings.oidc.scopesWithoutOpenid",
    message: "Must contain openid, which is what makes the sign-in OpenID Connect.",
  }),
  "settings.oidc.signInScheme": msg({
    context: "settings.oidc.signInScheme",
    message: "The handler would sign into ''{scheme}'', which bypasses local account linking entirely.",
  }),
  "settings.oidcTest.answered": msg({
    context: "settings.oidcTest.answered",
    message: "{url} answered {status} {reason}.",
  }),
  "settings.oidcTest.authorityInvalid": msg({
    context: "settings.oidcTest.authorityInvalid",
    message: "Enter the provider's https address, such as https://login.example.com/realms/ddt.",
  }),
  "settings.oidcTest.notDiscovery": msg({
    context: "settings.oidcTest.notDiscovery",
    message: "{url} is not the discovery document of an OpenID Connect provider.",
  }),
  "settings.oidcTest.otherIssuer": msg({
    context: "settings.oidcTest.otherIssuer",
    message: "The provider names itself {issuer}, not {authority}. Enter {issuer} as the authority.",
  }),
  "settings.oidcTest.reached": msg({
    context: "settings.oidcTest.reached",
    message: "The provider answered as {issuer}. Register {redirectUri} as the redirect URI of DDT's client there.",
  }),
  "settings.oidcTest.unreadable": msg({
    context: "settings.oidcTest.unreadable",
    message: "{url} could not be read: {error}",
  }),
  "settings.proxies.addressInvalid": msg({
    context: "settings.proxies.addressInvalid",
    message: "''{value}'' is not an IP address.",
  }),
  "settings.proxies.networkEverything": msg({
    context: "settings.proxies.networkEverything",
    message: "''{value}'' is every address there is, so any client could claim any address. Name the proxies' own network.",
  }),
  "settings.proxies.networkInvalid": msg({
    context: "settings.proxies.networkInvalid",
    message: "''{value}'' is not a network such as 10.20.0.0/24 with no address bits set past the prefix length.",
  }),
  "settings.proxies.networkWide": msg({
    context: "settings.proxies.networkWide",
    message: "{network} is wider than a /{prefix}. Every address in it counts as a trusted proxy address.",
  }),
  "settings.pxe.architectureUnknown": msg({
    context: "settings.pxe.architectureUnknown",
    message: "''{value}'' is not a client architecture. Use one of: {architectures}.",
  }),
  "settings.pxe.asciiMaxLength": msg({
    context: "settings.pxe.asciiMaxLength",
    message: "Must be ASCII and at most {max} characters.",
  }),
  "settings.pxe.bootDirectoryEmpty": msg({
    context: "settings.pxe.bootDirectoryEmpty",
    message: "Must not be empty. Leave it out for the folder boot in DDT:StorePath.",
  }),
  "settings.pxe.bootDirectoryHoldsKey": msg({
    context: "settings.pxe.bootDirectoryHoldsKey",
    message: "''{directory}'' holds the folder of the TLS certificate or key {file}, which would serve the key. Use a directory of its own, such as {suggested}.",
  }),
  "settings.pxe.bootDirectoryHoldsStore": msg({
    context: "settings.pxe.bootDirectoryHoldsStore",
    message: "''{directory}'' holds DDT:StorePath, {store}, which would serve the database and the key ring. Use a directory of its own, such as {suggested}.",
  }),
  "settings.pxe.bootDirectoryInKeys": msg({
    context: "settings.pxe.bootDirectoryInKeys",
    message: "''{directory}'' is in the key ring folder {keys}, which would serve its keys. Use a directory of its own, such as {suggested}.",
  }),
  "settings.pxe.bootDirectoryIsRoot": msg({
    context: "settings.pxe.bootDirectoryIsRoot",
    message: "''{directory}'' is the root of a filesystem, which would serve every file on it. Use a directory of its own, such as {suggested}.",
  }),
  "settings.pxe.bootFileRequired": msg({
    context: "settings.pxe.bootFileRequired",
    message: "Must be set.",
  }),
  "settings.pxe.bootFileUrl": msg({
    context: "settings.pxe.bootFileUrl",
    message: "Must be an absolute http or https URL.",
  }),
  "settings.pxe.bootUrl": msg({
    context: "settings.pxe.bootUrl",
    message: "{url} is not where DDT serves boot files, which is http://<server>:{port}/boot/. Keep it only if another server serves this file.",
  }),
  "settings.pxe.httpBootPortInvalid": msg({
    context: "settings.pxe.httpBootPortInvalid",
    message: "{port} is not a port number.",
  }),
  "settings.pxe.interfaceNotFound": msg({
    context: "settings.pxe.interfaceNotFound",
    message: "''{name}'' names no interface on {host}, which serves nothing for it.",
  }),
  "settings.pxe.methodForArchitecture": msg({
    context: "settings.pxe.methodForArchitecture",
    message: "Must be {method} for {architecture} clients.",
  }),
  "settings.pxe.methodInvalid": msg({
    context: "settings.pxe.methodInvalid",
    message: "Must be Tftp or Http.",
  }),
  "settings.pxe.notIpv4Address": msg({
    context: "settings.pxe.notIpv4Address",
    message: "''{value}'' is not an IPv4 address.",
  }),
  "settings.pxe.serverAddressRequired": msg({
    context: "settings.pxe.serverAddressRequired",
    message: "Required while EnableTftp is false, because this target uses Tftp. Name the TFTP server that serves it.",
  }),
  "settings.pxe.windowSizeRange": msg({
    context: "settings.pxe.windowSizeRange",
    message: "Must be between 1 and {max}.",
  }),
  "settings.reauthenticate.codeNeeded": msg({
    context: "settings.reauthenticate.codeNeeded",
    message: "Enter the code of your authenticator as well.",
  }),
  "settings.reauthenticate.lockedOut": msg({
    context: "settings.reauthenticate.lockedOut",
    message: "The account is locked out. Try again later.",
  }),
  "settings.reauthenticate.noPassword": msg({
    context: "settings.reauthenticate.noPassword",
    message: "This account signs in without a password DDT can check, so it cannot change these settings. Use an account with a local or directory password.",
  }),
  "settings.reauthenticate.notRight": msg({
    context: "settings.reauthenticate.notRight",
    message: "The password or the code is not right.",
  }),
  "settings.roleUnknown": msg({
    context: "settings.roleUnknown",
    message: "''{role}'' is not a DDT role. Use {roles}.",
  }),
  "settings.savedSince": msg({
    context: "settings.savedSince",
    message: "Someone saved {section} since you loaded it. Load it again.",
  }),
  "settings.secretCannotBeKept": msg({
    context: "settings.secretCannotBeKept",
    message: "No longer decrypts with this server's key ring, so it cannot be kept. Enter it again or clear it.",
  }),
  "settings.secretForNewServer": msg({
    context: "settings.secretForNewServer",
    message: "Enter it again for the new server: a stored secret goes only to the server it was entered for.",
  }),
  "settings.sendValues": msg({
    context: "settings.sendValues",
    message: "Send the values of the section.",
  }),
  "settings.storedSecretUnreadable": msg({
    context: "settings.storedSecretUnreadable",
    message: "The stored value no longer decrypts with this server's key ring. Enter it again.",
  }),
  "settings.storedValueUnreadable": msg({
    context: "settings.storedValueUnreadable",
    message: "The stored value cannot be read, so the default applies: {error}",
  }),
  "settings.valuesCannotBeChecked": msg({
    context: "settings.valuesCannotBeChecked",
    message: "The values cannot be checked: {error}",
  }),
  "template.installLinux": msg({
    context: "template.installLinux",
    message: "Install Linux",
  }),
  "template.installLinuxDescription": msg({
    context: "template.installLinuxDescription",
    message: "Writes a raw disk image, such as a distribution's cloud image, and a cloud-init seed that names the machine.",
  }),
  "template.installWindows": msg({
    context: "template.installWindows",
    message: "Install Windows",
  }),
  "template.installWindowsDescription": msg({
    context: "template.installWindowsDescription",
    message: "Partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file.",
  }),
  "template.installWindowsJoinDescription": msg({
    context: "template.installWindowsJoinDescription",
    message: "Partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file, then joins the domain in Windows.",
  }),
  "token.lifetime": msg({
    context: "token.lifetime",
    message: "A token lasts 1 to {max} days.",
  }),
  "token.nameTaken": msg({
    context: "token.nameTaken",
    message: "You have a token of that name already. Choose another name, or revoke that token first.",
  }),
  "token.noRole": msg({
    context: "token.noRole",
    message: "Your account has no role, so it cannot have a token.",
  }),
  "token.onlyOwnerRevokes": msg({
    context: "token.onlyOwnerRevokes",
    message: "Only the token's owner or an administrator can revoke it.",
  }),
  "token.roleTooHigh": msg({
    context: "token.roleTooHigh",
    message: "A token can do at most what you can, and you are {role}.",
  }),
  "uefiCa.name": msg({
    context: "uefiCa.name",
    message: "{cas, select, ca2011 {Microsoft's third-party UEFI CA 2011} ca2023 {Microsoft's third-party UEFI CA 2023} both {Microsoft's third-party UEFI CAs 2011 and 2023} other {Microsoft's third-party UEFI CA}}",
  }),
  "upload.beingChecked": msg({
    context: "upload.beingChecked",
    message: "This upload is being checked or written to. Ask again in a few seconds.",
  }),
  "upload.beyondLength": msg({
    context: "upload.beyondLength",
    message: "This chunk ends past the end of the file. Upload the file this session was created for.",
  }),
  "upload.chunkBusy": msg({
    context: "upload.chunkBusy",
    message: "Another request is using this upload. Wait a few seconds, then send the chunk again.",
  }),
  "upload.chunkSize": msg({
    context: "upload.chunkSize",
    message: "A chunk holds 1 to {max} bytes. Send the file in smaller chunks.",
  }),
  "upload.complete": msg({
    context: "upload.complete",
    message: "This upload is complete. Nothing more needs to be sent.",
  }),
  "upload.compressedDamaged": msg({
    context: "upload.compressedDamaged",
    message: "The compressed file is damaged: {detail}",
  }),
  "upload.contentLength": msg({
    context: "upload.contentLength",
    message: "Send every chunk with a Content-Length header.",
  }),
  "upload.conversionFailed": msg({
    context: "upload.conversionFailed",
    message: "The image could not be converted. {detail}",
  }),
  "upload.conversionOutOfSpace": msg({
    context: "upload.conversionOutOfSpace",
    message: "The store volume ran out of space while the image was converted. Free some space and complete the upload again.",
  }),
  "upload.cutOff": msg({
    context: "upload.cutOff",
    message: "The chunk ended before all of it arrived. Send it again.",
  }),
  "upload.diskFull": msg({
    context: "upload.diskFull",
    message: "The image store is full. Free space on the server's store volume, then continue the upload.",
  }),
  "upload.failed": msg({
    context: "upload.failed",
    message: "The upload could not be added to the library. Look at the server log, then complete the upload again.",
  }),
  "upload.fileName": msg({
    context: "upload.fileName",
    message: "The file name must have 1 to {max} characters and no control characters.",
  }),
  "upload.gone": msg({
    context: "upload.gone",
    message: "This upload no longer exists. Select the file again to upload it.",
  }),
  "upload.inUse": msg({
    context: "upload.inUse",
    message: "This upload is in use. Try again in a few seconds.",
  }),
  "upload.incomplete": msg({
    context: "upload.incomplete",
    message: "Not all of the file has arrived. Continue the upload from the offset in the Upload-Offset header.",
  }),
  "upload.kind": msg({
    context: "upload.kind",
    message: "Choose an image, a driver package or a files package.",
  }),
  "upload.length": msg({
    context: "upload.length",
    message: "The file must not be empty and must fit on the server's store volume.",
  }),
  "upload.noQemuImg": msg({
    context: "upload.noQemuImg",
    message: "This is a qcow2 image, and qemu-img is not installed on the server. Install qemu-img there and complete the upload again, or convert the file with qemu-img convert -O raw <file> disk.raw and upload disk.raw.",
  }),
  "upload.noSpace": msg({
    context: "upload.noSpace",
    message: "The image store needs {required} free for this upload but has {available}. Free space on the server's store volume or discard unfinished uploads, then try again.",
  }),
  "upload.noXz": msg({
    context: "upload.noXz",
    message: "This file is compressed with xz, which is not installed on the server. Install xz there and complete the upload again, or unpack the file with xz -d and upload the disk image it holds.",
  }),
  "upload.notAnImage": msg({
    context: "upload.notAnImage",
    message: "This file is neither a WIM image nor a disk image with a GUID partition table. Upload a WIM or ESD file, or a disk image such as a distribution's cloud image.",
  }),
  "upload.offsetHeader": msg({
    context: "upload.offsetHeader",
    message: "Send the position of the chunk in the file in the Upload-Offset header.",
  }),
  "upload.offsetMismatch": msg({
    context: "upload.offsetMismatch",
    message: "The server holds a different part of this file. Continue from the offset in the Upload-Offset header.",
  }),
  "upload.qcow2Backing": msg({
    context: "upload.qcow2Backing",
    message: "The qcow2 image depends on a backing file. Make a standalone image with qemu-img convert -O qcow2 <file> standalone.qcow2 and upload that.",
  }),
  "upload.qcow2Encrypted": msg({
    context: "upload.qcow2Encrypted",
    message: "The qcow2 image is encrypted. Upload an image that is not.",
  }),
  "upload.qcow2ExternalData": msg({
    context: "upload.qcow2ExternalData",
    message: "The qcow2 image keeps its data in an external file. Convert it with qemu-img convert -O raw <file> disk.raw and upload disk.raw.",
  }),
  "upload.qcow2TooShort": msg({
    context: "upload.qcow2TooShort",
    message: "The qcow2 image is too short to be one.",
  }),
  "upload.restarted": msg({
    context: "upload.restarted",
    message: "The server lost part of this upload. Send the file again from the start.",
  }),
  "upload.serverStopping": msg({
    context: "upload.serverStopping",
    message: "The server is stopping. Complete the upload again once it is back.",
  }),
  "upload.stalled": msg({
    context: "upload.stalled",
    message: "The chunk stopped arriving for too long. Send it again.",
  }),
  "upload.unreadableFormat": msg({
    context: "upload.unreadableFormat",
    message: "This is a {format} disk image, which DDT does not read. Convert it with qemu-img convert -O raw <file> disk.raw and upload disk.raw, or upload the distribution's raw or qcow2 image.",
  }),
  "user.changedWhileSaving": msg({
    context: "user.changedWhileSaving",
    message: "The account changed while this was saved. Look at it again.",
  }),
  "user.directoryPassword": msg({
    context: "user.directoryPassword",
    message: "{name} signs in with its directory password. Reset it in the directory.",
  }),
  "user.directoryProvidesNames": msg({
    context: "user.directoryProvidesNames",
    message: "The directory provides the display name and email address of {name}. Change them there; DDT takes them over at its next sign-in.",
  }),
  "user.displayNameEmpty": msg({
    context: "user.displayNameEmpty",
    message: "Enter the name DDT shows for the account.",
  }),
  "user.displayNameTooLong": msg({
    context: "user.displayNameTooLong",
    message: "A display name can have at most {max} characters.",
  }),
  "user.email": msg({
    context: "user.email",
    message: "Enter an email address such as jane@corp.example, or leave it empty.",
  }),
  "user.lastAdministrator": msg({
    context: "user.lastAdministrator",
    message: "{name} is the last enabled administrator. Make another account an administrator first.",
  }),
  "user.nameEmpty": msg({
    context: "user.nameEmpty",
    message: "Enter a user name.",
  }),
  "user.nameTaken": msg({
    context: "user.nameTaken",
    message: "There is an account named {name} already.",
  }),
  "user.nameTooLong": msg({
    context: "user.nameTooLong",
    message: "A user name can have at most {max} characters.",
  }),
  "user.ownAdministratorRole": msg({
    context: "user.ownAdministratorRole",
    message: "You cannot take the Administrator role from your own account. Another administrator can.",
  }),
  "user.ownDelete": msg({
    context: "user.ownDelete",
    message: "You cannot delete your own account. Another administrator can.",
  }),
  "user.ownDisable": msg({
    context: "user.ownDisable",
    message: "You cannot disable your own account. Another administrator can.",
  }),
  "user.ownPassword": msg({
    context: "user.ownPassword",
    message: "Change your own password on the Account page.",
  }),
  "user.ownSecondFactor": msg({
    context: "user.ownSecondFactor",
    message: "Turn off your own second factor on the Account page.",
  }),
  "user.role": msg({
    context: "user.role",
    message: "Choose Administrator, Operator or Viewer.",
  }),
  "user.roleFromDirectoryGroupMap": msg({
    context: "user.roleFromDirectoryGroupMap",
    message: "The role of {name} comes from its directory groups through the directory's group map, at each sign-in. Change its groups in the directory, or the map on the Sign-in page.",
  }),
  "user.roleFromSingleSignOnGroupMap": msg({
    context: "user.roleFromSingleSignOnGroupMap",
    message: "The role of {name} comes from its single sign-on groups through the single sign-on group map, at each sign-in. Change its groups at the provider, or the map on the Sign-in page.",
  }),
  "user.singleSignOnPassword": msg({
    context: "user.singleSignOnPassword",
    message: "{name} signs in through single sign-on and has no password in DDT.",
  }),
  "wim.compressedList": msg({
    context: "wim.compressedList",
    message: "This WIM file stores its image list compressed, which DDT cannot read.",
  }),
  "wim.encrypted": msg({
    context: "wim.encrypted",
    message: "This image is encrypted (an ESD from Windows Update) and cannot be applied.",
  }),
  "wim.incomplete": msg({
    context: "wim.incomplete",
    message: "This WIM file is incomplete or damaged.",
  }),
  "wim.listDamaged": msg({
    context: "wim.listDamaged",
    message: "The image list in this WIM file is damaged.",
  }),
  "wim.listMismatch": msg({
    context: "wim.listMismatch",
    message: "The image list in this WIM file does not match its header.",
  }),
  "wim.listNotUtf16": msg({
    context: "wim.listNotUtf16",
    message: "The image list in this WIM file is not UTF-16 text, which DDT cannot read.",
  }),
  "wim.listTooLarge": msg({
    context: "wim.listTooLarge",
    message: "The image list in this WIM file is too large.",
  }),
  "wim.noX64Image": msg({
    context: "wim.noX64Image",
    message: "This WIM holds no x64 Windows image.",
  }),
  "wim.notAWim": msg({
    context: "wim.notAWim",
    message: "This file is not a WIM image.",
  }),
  "wim.pipable": msg({
    context: "wim.pipable",
    message: "Pipable WIM files are not supported. Export the image into a regular WIM first.",
  }),
  "wim.split": msg({
    context: "wim.split",
    message: "Split WIM files (.swm) are not supported. Export the image into a single WIM first.",
  }),
  "wim.version": msg({
    context: "wim.version",
    message: "This WIM file uses format version 0x{version}, which DDT does not support.",
  }),
};
