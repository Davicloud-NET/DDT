// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import type { CurrentUser } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { NumberField } from "@/ui/Controls";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";
import {
  fieldErrors,
  formError,
  highestRole,
  roleDescription,
  roleLabel,
  rolesUpTo,
} from "@/users/userView";

import { createToken, upsertToken, type CreatedApiToken, type TokenRole } from "./tokens";
import { TOKEN_DAYS } from "./tokenView";

const FIELDS = ["name", "role", "expiresInDays"];

// Makes an API token for the signed-in person, with at most their own role. The server shows its secret once, in the
// answer; this dialog shows it until it closes and then forgets it. The list gets the token without its secret.
export function MakeTokenDialog({ user, onClose }: { user: CurrentUser; onClose: () => void }) {
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

  const errors = (field: string) => fieldErrors(make.error, field);
  const general = formError(make.error, FIELDS);
  const expires = Number.isFinite(days)
    ? new Date(now + days * 86_400_000).toLocaleDateString(formattingLocale(), {
        dateStyle: "long",
      })
    : "";

  if (created !== null) {
    const tokenName = created.token.name;
    const tokenRole = roleLabel(created.token.role);

    return (
      <Dialog
        isOpen
        onOpenChange={(open) => {
          if (!open) {
            onClose();
          }
        }}
        title={<Trans>Copy the token {tokenName}</Trans>}
        footer={
          <Button variant="primary" onPress={onClose}>
            <Trans>Done</Trans>
          </Button>
        }
      >
        <Notice tone="attention">
          <Trans>
            DDT shows this token only now and keeps nothing but a hash of it. If it is lost, revoke
            it and make a new one.
          </Trans>
        </Notice>
        <SecretValue label={<Trans>Token</Trans>} value={created.secret} />
        <p>
          <Trans>
            A script sends it in the Authorization header: Bearer, a space, then the token. It has
            the role {tokenRole}, and never more rights than your account has.
          </Trans>
        </p>
      </Dialog>
    );
  }

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Make an API token</Trans>}
      isBusy={make.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={make.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form="make-token"
            variant="primary"
            isDisabled={make.isPending || !Number.isFinite(days)}
          >
            <Trans>Make token</Trans>
          </Button>
        </>
      }
    >
      <form
        id="make-token"
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (Number.isFinite(days)) {
            make.mutate();
          }
        }}
      >
        <p>
          <Trans>
            A token lets a script call DDT's API as you. It stops working when it expires, when it
            is revoked, or when your account is disabled.
          </Trans>
        </p>
        <TextField
          label={<Trans>Name</Trans>}
          hint={<Trans>What uses it, such as the inventory script.</Trans>}
          autoFocus
          autoComplete="off"
          maxLength={64}
          value={name}
          onChange={setName}
          isRequired
          isInvalid={errors("name").length > 0}
          errorMessage={errors("name").join(" ")}
        />
        <Select
          label={<Trans>Role</Trans>}
          hint={<Trans>At most your own. Give it no more than the script needs.</Trans>}
          value={role}
          onChange={(key) => {
            if (key !== null) {
              setRole(String(key) as TokenRole);
            }
          }}
          isInvalid={errors("role").length > 0}
          errorMessage={errors("role").join(" ")}
        >
          {roles.map((option) => (
            <ListBoxItem
              key={option}
              id={option}
              textValue={roleLabel(option)}
              description={roleDescription(option)}
            >
              {roleLabel(option)}
            </ListBoxItem>
          ))}
        </Select>
        <NumberField
          label={<Trans>Lifetime in days</Trans>}
          hint={
            expires === "" ? (
              <Trans>1 to 365 days.</Trans>
            ) : (
              <Trans>1 to 365 days. It stops working on {expires}.</Trans>
            )
          }
          minValue={TOKEN_DAYS.min}
          maxValue={TOKEN_DAYS.max}
          value={days}
          onChange={setDays}
          isInvalid={errors("expiresInDays").length > 0}
          errorMessage={errors("expiresInDays").join(" ")}
        />
        {general !== null ? <Notice tone="fail">{general}</Notice> : null}
      </form>
    </Dialog>
  );
}
