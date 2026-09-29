// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  updateUser,
  upsertUser,
  type UpdateUserRequest,
  type UserRole,
  type UserView,
} from "./users";
import { fieldErrors, formError, groupsDecideRole, roleNote } from "./userView";

const UPDATE_FIELDS = ["displayName", "email", "role"];

export type ChangeUserState = ReturnType<typeof useChangeUser>;

// The change dialog's fields and the update call, which patches the list with the answer.
export function useChangeUser({
  user,
  isSelf,
  onClose,
}: {
  user: UserView;
  isSelf: boolean;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(user.displayName ?? "");
  const [email, setEmail] = useState(user.email ?? "");
  const [role, setRole] = useState<UserRole | null>(user.role);

  const directory = user.source === "Directory";
  const roleLocked = groupsDecideRole(user) || isSelf;

  // Only what changed is sent. An empty text clears the name or the address, and a field left out stays as it is.
  const request: UpdateUserRequest = {
    ...(!directory && displayName.trim() !== (user.displayName ?? "")
      ? { displayName: displayName.trim() }
      : {}),
    ...(!directory && email.trim() !== (user.email ?? "") ? { email: email.trim() } : {}),
    ...(!roleLocked && role !== null && role !== user.role ? { role } : {}),
  };

  const save = useMutation({
    mutationFn: () => updateUser(user.id, request),
    onSuccess: (saved) => {
      upsertUser(queryClient, saved);
      onClose();
    },
  });

  return {
    formId: `user-${user.id}`,
    displayName,
    setDisplayName,
    email,
    setEmail,
    role,
    setRole,
    directory,
    roleLocked,
    roleNote: roleNote(user, isSelf),
    changed: Object.keys(request).length > 0,
    save,
    errors: (field: string) => fieldErrors(save.error, field),
    general: formError(save.error, UPDATE_FIELDS),
  };
}
