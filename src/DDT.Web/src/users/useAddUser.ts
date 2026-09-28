// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { createUser, upsertUser, type CreatedUser, type UserRole } from "./users";
import { fieldErrors, formError } from "./userView";

const CREATE_FIELDS = ["userName", "displayName", "email", "role"];

export type AddUserState = ReturnType<typeof useAddUser>;

// The add dialog's fields and the create call, which puts the new account into the list.
export function useAddUser(onCreated: (created: CreatedUser) => void) {
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

  return {
    userName,
    setUserName,
    displayName,
    setDisplayName,
    email,
    setEmail,
    role,
    setRole,
    create,
    errors: (field: string) => fieldErrors(create.error, field),
    general: formError(create.error, CREATE_FIELDS),
  };
}
