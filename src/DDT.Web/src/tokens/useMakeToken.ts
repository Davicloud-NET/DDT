// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import type { CurrentUser } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { useNow } from "@/lib/useNow";
import { fieldErrors, formError, highestRole, rolesUpTo } from "@/users/userView";

import { createToken, upsertToken, type CreatedApiToken, type TokenRole } from "./tokens";
import { TOKEN_DAYS } from "./tokenView";

const FIELDS = ["name", "role", "expiresInDays"];

export type MakeTokenState = ReturnType<typeof useMakeToken>;

// The make dialog's fields and the create call. The server only returns the secret in this answer, so it's kept
// here until the dialog closes. The list gets the token without it.
export function useMakeToken(user: CurrentUser) {
  const queryClient = useQueryClient();
  const roles = rolesUpTo(highestRole(user.roles));
  const [name, setName] = useState("");
  const [role, setRole] = useState<TokenRole>(roles[roles.length - 1] ?? "Viewer");
  const [days, setDays] = useState(TOKEN_DAYS.default);
  const [created, setCreated] = useState<CreatedApiToken | null>(null);
  const now = useNow(60_000);

  const make = useMutation({
    mutationFn: () => createToken({ name: name.trim(), role, expiresInDays: days }),
    onSuccess: (answer) => {
      upsertToken(queryClient, answer.token, user.id);
      setCreated(answer);
    },
  });

  return {
    roles,
    name,
    setName,
    role,
    setRole,
    days,
    setDays,
    created,
    make,
    errors: (field: string) => fieldErrors(make.error, field),
    general: formError(make.error, FIELDS),
    expires: Number.isFinite(days)
      ? new Date(now + days * 86_400_000).toLocaleDateString(formattingLocale(), {
          dateStyle: "long",
        })
      : "",
  };
}
