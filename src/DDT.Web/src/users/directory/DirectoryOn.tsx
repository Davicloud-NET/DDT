// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { Facts } from "@/ui/Facts";

import type { DirectoryView } from "../users";
import { roleLabel } from "../userView";
import { DirectoryUserCheck } from "./DirectoryUserCheck";
import { MappedGroupSearch } from "./MappedGroupSearch";

// The directory panel while directory sign-in is on.
export function DirectoryOn({ directory }: { directory: DirectoryView }) {
  const map = directory.groupRoleMap;

  return (
    <>
      <Facts
        items={[
          { label: <Trans>Server</Trans>, value: directory.host ?? "", mono: true },
          { label: <Trans>Search base</Trans>, value: directory.baseDn ?? "", mono: true },
        ]}
      />
      <p className="max-w-[80ch] text-ink-2">
        {map.length === 0 ? (
          <Trans>
            No group is mapped to a role, so administrators choose the role of each directory
            account on this page, and a new one reaches nothing until it has one. The map is set on
            the{" "}
            <Link to="/admin/sign-in" className="font-semibold text-ink underline">
              Sign-in
            </Link>{" "}
            page.
          </Trans>
        ) : (
          <Trans>
            At each sign-in, a directory account gets the highest role that its groups below give
            it, and an account in none of them cannot sign in. The map is set on the{" "}
            <Link to="/admin/sign-in" className="font-semibold text-ink underline">
              Sign-in
            </Link>{" "}
            page.
          </Trans>
        )}
      </p>
      {map.length > 0 ? <GroupRoleMap map={map} /> : null}
      <div className="grid gap-6 pt-2 lg:grid-cols-2">
        <MappedGroupSearch directory={directory} />
        <DirectoryUserCheck />
      </div>
    </>
  );
}

function GroupRoleMap({ map }: { map: DirectoryView["groupRoleMap"] }) {
  const { t } = useLingui();

  return (
    <ul
      aria-label={t`Groups and the roles they give`}
      className="flex flex-col divide-y divide-line-soft rounded-key shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
    >
      {map.map((entry) => (
        <li key={entry.group} className="flex items-center gap-4 px-3.5 py-2.5">
          <span className="flex min-w-0 flex-1 flex-col">
            {entry.name === null ? (
              <span className="type-label text-attention-text">
                <Trans>Not found in the directory</Trans>
              </span>
            ) : (
              <span className="truncate type-label text-ink">{entry.name}</span>
            )}
            <span className="type-data text-[12.5px] break-all text-muted">{entry.group}</span>
          </span>
          <span className="shrink-0 type-label text-ink">{roleLabel(entry.role)}</span>
        </li>
      ))}
    </ul>
  );
}
