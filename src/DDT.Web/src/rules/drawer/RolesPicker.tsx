// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import { Link } from "@tanstack/react-router";
import { useContext, useId } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import type { MachineRoleView } from "@/roles/roles";
import { EditorLock } from "@/sequences/editorLock";
import { fieldFindings, type Findings } from "@/sequences/problems";
import { Menu, MenuItem } from "@/ui/Menu";

import { RoleTag } from "./RoleTag";

interface RolesPickerProps {
  roleIds: readonly string[];
  roles: readonly MachineRoleView[];
  findings: Findings;
  onChange: (roleIds: string[]) => void;
}

// The machine roles a rule gives, as tags that can be removed, and a menu to add the others.
export function RolesPicker({ roleIds, roles, findings, onChange }: RolesPickerProps) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const labelId = useId();
  const others = roles.filter((role) => !roleIds.includes(role.id));
  const messages = [
    ...fieldFindings(findings, "roleIds").problems,
    ...roleIds.flatMap((_, index) => fieldFindings(findings, `roleIds[${String(index)}]`).problems),
  ];

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field="roleIds"
      className="flex flex-col gap-2"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Gives machine roles</Trans>
      </span>
      <div className="flex flex-wrap items-center gap-1.5">
        {roleIds.map((id, index) => {
          const role = roles.find((candidate) => candidate.id === id);
          const field = `roleIds[${String(index)}]`;

          return (
            <RoleTag
              key={id}
              roleName={role?.name ?? t`A machine role that is gone`}
              field={field}
              hasProblem={fieldFindings(findings, field).problems.length > 0}
              locked={locked}
              onRemove={() => {
                onChange(roleIds.filter((other) => other !== id));
              }}
            />
          );
        })}
        {!locked && others.length > 0 ? (
          <MenuTrigger>
            <AriaButton className="flex cursor-pointer items-center gap-1.5 rounded-key px-1.5 py-1 type-label text-ink key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus">
              <IconPlus aria-hidden="true" size={14} stroke={2} />
              <Trans>Add a role</Trans>
            </AriaButton>
            <Menu
              placement="bottom start"
              aria-label={t`Machine roles to add`}
              onAction={(key) => {
                onChange([...roleIds, String(key)]);
              }}
            >
              {others.map((role) => (
                <MenuItem key={role.id} id={role.id} textValue={role.name}>
                  {role.name}
                </MenuItem>
              ))}
            </Menu>
          </MenuTrigger>
        ) : null}
      </div>
      {roleIds.length === 0 && (locked || roles.length > 0) ? (
        <p className="type-small text-muted">
          <Trans>No machine roles.</Trans>
        </p>
      ) : null}
      {!locked && roles.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>
            There are no machine roles yet. Add them under{" "}
            <Link to="/deployment/machine-roles" className="text-ink underline">
              Deployment, Machine roles
            </Link>
            .
          </Trans>
        </p>
      ) : null}
      {messages.map((message) => (
        <p key={message} className="type-small text-fail-text">
          {message}
        </p>
      ))}
    </div>
  );
}
