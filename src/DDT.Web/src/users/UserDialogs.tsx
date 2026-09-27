// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import {
  createUser,
  updateUser,
  upsertUser,
  type CreatedUser,
  type UpdateUserRequest,
  type UserRole,
  type UserView,
} from "./users";
import {
  fieldErrors,
  formError,
  roleDescription,
  roleLabel,
  roleLockReason,
  ROLES,
  shownName,
} from "./userView";

function RoleOptions() {
  return (
    <>
      {ROLES.map((role) => (
        <ListBoxItem
          key={role}
          id={role}
          textValue={roleLabel(role)}
          description={roleDescription(role)}
        >
          {roleLabel(role)}
        </ListBoxItem>
      ))}
    </>
  );
}

const CREATE_FIELDS = ["userName", "displayName", "email", "role"];

// Adds a local account. The server makes up its password, which the page shows once when this dialog has closed.
export function AddUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (created: CreatedUser) => void;
}) {
  const queryClient = useQueryClient();
  const [userName, setUserName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<UserRole>("Viewer");

  const create = useMutation({
    mutationFn: () =>
      createUser({
        userName: userName.trim(),
        displayName: displayName.trim(),
        email: email.trim() === "" ? null : email.trim(),
        role,
      }),
    onSuccess: (created) => {
      upsertUser(queryClient, created.user);
      onCreated(created);
    },
  });

  const errors = (field: string) => fieldErrors(create.error, field);
  const general = formError(create.error, CREATE_FIELDS);

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Add a local account</Trans>}
      isBusy={create.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={create.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button type="submit" form="add-user" variant="primary" isDisabled={create.isPending}>
            <Trans>Add account</Trans>
          </Button>
        </>
      }
    >
      <form
        id="add-user"
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate();
        }}
      >
        <p>
          <Trans>
            DDT makes up a password for the account and shows it once. Its owner replaces it at the
            first sign-in. Directory and single sign-on accounts are not added here: they appear
            after their first sign-in.
          </Trans>
        </p>
        <TextField
          label={<Trans>User name</Trans>}
          hint={<Trans>What the account signs in with.</Trans>}
          autoFocus
          autoComplete="off"
          spellCheck="false"
          value={userName}
          onChange={setUserName}
          isRequired
          isInvalid={errors("userName").length > 0}
          errorMessage={errors("userName").join(" ")}
        />
        <TextField
          label={<Trans>Name</Trans>}
          hint={<Trans>The name DDT shows for the account, such as the person's full name.</Trans>}
          value={displayName}
          onChange={setDisplayName}
          isRequired
          isInvalid={errors("displayName").length > 0}
          errorMessage={errors("displayName").join(" ")}
        />
        <TextField
          label={<Trans>Email address</Trans>}
          hint={<Trans>Optional.</Trans>}
          type="email"
          value={email}
          onChange={setEmail}
          isInvalid={errors("email").length > 0}
          errorMessage={errors("email").join(" ")}
        />
        <Select
          label={<Trans>Role</Trans>}
          value={role}
          onChange={(key) => {
            if (key !== null) {
              setRole(String(key) as UserRole);
            }
          }}
          isInvalid={errors("role").length > 0}
          errorMessage={errors("role").join(" ")}
        >
          <RoleOptions />
        </Select>
        {general !== null ? <Notice tone="fail">{general}</Notice> : null}
      </form>
    </Dialog>
  );
}

const UPDATE_FIELDS = ["displayName", "email", "role"];

// Changes an account's name, email address and role. The directory provides the name and address of its accounts, and
// groups that decide a role leave nothing to choose here; both are shown with the reason. An account's own role is not
// changed here, since the server refuses to take the Administrator role from the one asking.
export function ChangeUserDialog({
  user,
  isSelf,
  onClose,
}: {
  user: UserView;
  isSelf: boolean;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(user.displayName ?? "");
  const [email, setEmail] = useState(user.email ?? "");
  const [role, setRole] = useState<UserRole | null>(user.role);

  const directory = user.source === "Directory";
  const lockReason = roleLockReason(user);
  const roleLocked = lockReason !== null || isSelf;

  // Only what changed is sent: an empty text clears the name or the address, a field left out stays as it is.
  const request: UpdateUserRequest = {
    ...(!directory && displayName.trim() !== (user.displayName ?? "")
      ? { displayName: displayName.trim() }
      : {}),
    ...(!directory && email.trim() !== (user.email ?? "") ? { email: email.trim() } : {}),
    ...(!roleLocked && role !== null && role !== user.role ? { role } : {}),
  };
  const changed = Object.keys(request).length > 0;

  const save = useMutation({
    mutationFn: () => updateUser(user.id, request),
    onSuccess: (saved) => {
      upsertUser(queryClient, saved);
      onClose();
    },
  });

  const errors = (field: string) => fieldErrors(save.error, field);
  const general = formError(save.error, UPDATE_FIELDS);
  const name = shownName(user);
  const formId = `user-${user.id}`;

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Change {name}</Trans>}
      isBusy={save.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={save.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={save.isPending || !changed}
          >
            <Trans>Save changes</Trans>
          </Button>
        </>
      }
    >
      <form
        id={formId}
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();

          if (changed) {
            save.mutate();
          }
        }}
      >
        <TextField
          label={<Trans>Name</Trans>}
          value={displayName}
          onChange={setDisplayName}
          isDisabled={directory}
          hint={
            directory ? (
              <Trans>
                The directory provides the name and email address. DDT takes them over at each
                sign-in.
              </Trans>
            ) : (
              <Trans>Left empty, DDT shows the user name.</Trans>
            )
          }
          isInvalid={errors("displayName").length > 0}
          errorMessage={errors("displayName").join(" ")}
        />
        <TextField
          label={<Trans>Email address</Trans>}
          type="email"
          value={email}
          onChange={setEmail}
          isDisabled={directory}
          isInvalid={errors("email").length > 0}
          errorMessage={errors("email").join(" ")}
        />
        <Select
          label={<Trans>Role</Trans>}
          value={role}
          placeholder={t`No role`}
          isDisabled={roleLocked}
          hint={
            lockReason ??
            (isSelf ? (
              <Trans>You cannot change your own role. Another administrator can.</Trans>
            ) : user.roleFrom === "Provisioned" ? (
              <Trans>
                Single sign-on gave this role when it created the account. A role chosen here stays
                until an administrator changes it.
              </Trans>
            ) : user.role === null ? (
              <Trans>The account has no role yet, so it reaches nothing.</Trans>
            ) : undefined)
          }
          onChange={(key) => {
            if (key !== null) {
              setRole(String(key) as UserRole);
            }
          }}
          isInvalid={errors("role").length > 0}
          errorMessage={errors("role").join(" ")}
        >
          <RoleOptions />
        </Select>
        {general !== null ? <Notice tone="fail">{general}</Notice> : null}
      </form>
    </Dialog>
  );
}

// The password the server made up for a new account or a reset, shown once. Closing the dialog forgets it.
export function PasswordDialog({
  shown,
  onClose,
}: {
  shown: { name: string; password: string; reset: boolean } | null;
  onClose: () => void;
}) {
  const name = shown?.name ?? "";

  return (
    <Dialog
      isOpen={shown !== null}
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={
        shown?.reset === true ? (
          <Trans>New password for {name}</Trans>
        ) : (
          <Trans>Password for {name}</Trans>
        )
      }
      footer={
        <Button variant="primary" onPress={onClose}>
          <Trans>Done</Trans>
        </Button>
      }
    >
      {shown?.reset === true ? (
        <p>
          <Trans>
            The old password no longer works, and {name} is signed out within a minute. Hand over
            this one in person or through a channel you trust.
          </Trans>
        </p>
      ) : (
        <p>
          <Trans>
            The account is ready. Hand over this password in person or through a channel you trust.
          </Trans>
        </p>
      )}
      <SecretValue label={<Trans>One-time password</Trans>} value={shown?.password ?? ""} />
      <Notice tone="attention">
        <Trans>
          DDT shows this password only now. At the next sign-in, {name} has to set a password of
          their own, and can do nothing else until then.
        </Trans>
      </Notice>
    </Dialog>
  );
}
