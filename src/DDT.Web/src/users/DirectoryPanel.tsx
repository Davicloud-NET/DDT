// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";

import { Button } from "@/ui/Button";
import { SearchField } from "@/ui/Controls";
import { Facts, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import {
  checkDirectoryUser,
  directoryCheckText,
  directoryQuery,
  findGroups,
  type DirectoryCheck,
  type DirectoryView,
} from "./users";
import { fieldErrors, roleLabel } from "./userView";

// How long typing rests before the directory is asked, so a group name is not searched letter by letter.
const SEARCH_DELAY_MS = 300;

// The directory sign-in as the Sign-in page sets it up: which groups give which role, with a search for groups and a
// check of what a sign-in would give a user. Both ask the directory with DDT's bind account; the server answers 409
// while the directory is off or incomplete and 502 when it cannot reach it, with the reason as the message.
export function DirectoryPanel() {
  const directory = useQuery(directoryQuery);

  return (
    <Panel title={<Trans>Directory groups</Trans>}>
      {directory.isPending ? (
        <>
          <Skeleton className="h-5 w-1/3" />
          <Skeleton className="h-5 w-1/2" />
        </>
      ) : directory.isError ? (
        <Notice tone="fail" title={<Trans>The directory settings could not be loaded.</Trans>}>
          {directory.error.message}
        </Notice>
      ) : directory.data.enabled ? (
        <DirectoryOn directory={directory.data} />
      ) : (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            Sign-in through a directory is off. It is turned on and set up on the{" "}
            <Link to="/admin/sign-in" className="font-semibold text-ink underline">
              Sign-in
            </Link>{" "}
            page.
          </Trans>
        </p>
      )}
    </Panel>
  );
}

function DirectoryOn({ directory }: { directory: DirectoryView }) {
  const { t } = useLingui();
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
      {map.length > 0 ? (
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
      ) : null}
      <div className="grid gap-6 pt-2 lg:grid-cols-2">
        <GroupSearch directory={directory} />
        <UserCheck />
      </div>
    </>
  );
}

function GroupSearch({ directory }: { directory: DirectoryView }) {
  const { t } = useLingui();
  const [query, setQuery] = useState("");
  const [asked, setAsked] = useState("");

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setAsked(query.trim());
    }, SEARCH_DELAY_MS);

    return () => {
      window.clearTimeout(timer);
    };
  }, [query]);

  const groups = useQuery({
    queryKey: ["directory", "groups", asked],
    queryFn: () => findGroups(asked),
    enabled: asked !== "",
    retry: false,
    staleTime: 60_000,
  });

  const mapped = new Map(
    directory.groupRoleMap.map((entry) => [entry.group.toLowerCase(), entry.role]),
  );

  return (
    <section className="flex flex-col gap-3">
      <h3 className="type-label text-ink">
        <Trans>Find a group</Trans>
      </h3>
      <SearchField
        label={t`Find a directory group`}
        placeholder={t`Group name`}
        value={query}
        onChange={setQuery}
        className="w-full"
      />
      {asked === "" ? (
        <p className="type-small text-muted">
          <Trans>Type part of a group's name to see its distinguished name for the map.</Trans>
        </p>
      ) : groups.isPending ? (
        <Skeleton className="h-5 w-2/3" />
      ) : groups.isError ? (
        <Notice tone="fail">{groups.error.message}</Notice>
      ) : groups.data.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>No group matches.</Trans>
        </p>
      ) : (
        <ul
          aria-label={t`Directory groups found`}
          className="flex max-h-96 flex-col divide-y divide-line-soft overflow-auto rounded-key bg-well"
        >
          {groups.data.map((group) => {
            const role = mapped.get(group.distinguishedName.toLowerCase());
            const label = role === undefined ? null : roleLabel(role);

            return (
              <li key={group.distinguishedName} className="flex flex-col gap-0.5 px-3 py-2">
                <span className="flex items-baseline gap-2">
                  <span className="min-w-0 flex-1 truncate type-label text-ink">
                    {group.name ?? group.distinguishedName}
                  </span>
                  {label !== null ? (
                    <span className="shrink-0 type-small text-ink-2">
                      <Trans>Gives {label}</Trans>
                    </span>
                  ) : null}
                </span>
                <span className="type-data text-[12.5px] break-all text-muted select-all">
                  {group.distinguishedName}
                </span>
                {group.description !== null && group.description !== "" ? (
                  <span className="type-small text-ink-2">{group.description}</span>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

// What a sign-in with a user name would give, found without the user's password.
function UserCheck() {
  const [userName, setUserName] = useState("");

  const check = useMutation({
    mutationFn: () => checkDirectoryUser(userName.trim()),
  });

  const errors = fieldErrors(check.error, "userName");

  return (
    <section className="flex flex-col gap-3">
      <h3 className="type-label text-ink">
        <Trans>Check a user</Trans>
      </h3>
      <form
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          check.mutate();
        }}
      >
        <TextField
          label={<Trans>User name</Trans>}
          autoComplete="off"
          spellCheck="false"
          value={userName}
          onChange={(value) => {
            setUserName(value);
          }}
          isInvalid={errors.length > 0}
          errorMessage={errors.join(" ")}
          className="min-w-0 flex-1"
        />
        <Button type="submit" isDisabled={check.isPending || userName.trim() === ""}>
          <Trans>Check</Trans>
        </Button>
      </form>
      {check.isError && errors.length === 0 ? (
        <Notice tone="fail">{check.error.message}</Notice>
      ) : null}
      {check.isSuccess ? <CheckResult result={check.data} /> : null}
    </section>
  );
}

// The first value of a distinguished name, such as "Deployment admins" of CN=Deployment admins,OU=Groups,DC=corp.
function firstValue(distinguishedName: string): string {
  const first = distinguishedName.split(/(?<!\\),/)[0] ?? distinguishedName;

  return first.slice(first.indexOf("=") + 1).replace(/\\(.)/g, "$1");
}

function CheckResult({ result }: { result: DirectoryCheck }) {
  if (!result.found) {
    return <Notice tone="attention">{directoryCheckText(result)}</Notice>;
  }

  const count = result.groups.length;
  const role = result.role === null ? null : roleLabel(result.role);

  return (
    <div className="flex flex-col gap-3 rounded-key bg-well p-3.5">
      <Facts
        items={[
          ...(result.displayName === null
            ? []
            : [{ label: <Trans>Name</Trans>, value: result.displayName }]),
          {
            label: <Trans>Entry</Trans>,
            value: result.distinguishedName ?? "",
            mono: true,
          },
          {
            label: <Trans>Role at sign-in</Trans>,
            value:
              role === null ? (
                <span className="text-attention-text">
                  <Trans>None</Trans>
                </span>
              ) : (
                <span className="type-label">{role}</span>
              ),
          },
          {
            label: <Trans>Mapped groups</Trans>,
            value:
              result.matches.length === 0 ? (
                <span className="text-muted">
                  <Trans>None</Trans>
                </span>
              ) : (
                <ul className="flex flex-col gap-0.5">
                  {result.matches.map((match) => (
                    <MatchLine key={match.group} group={match.group} role={match.role} />
                  ))}
                </ul>
              ),
          },
          {
            label: <Trans>All groups</Trans>,
            value:
              count === 0 ? (
                <span className="text-muted">
                  <Trans>None</Trans>
                </span>
              ) : (
                <details>
                  <summary className="cursor-pointer text-ink-2">
                    {plural(count, { one: "# group", other: "# groups" })}
                  </summary>
                  <ul className="mt-1 flex flex-col gap-0.5">
                    {result.groups.map((group) => (
                      <li key={group} className="type-data text-[12.5px] break-all text-muted">
                        {group}
                      </li>
                    ))}
                  </ul>
                </details>
              ),
          },
        ]}
      />
      <p className="type-small text-ink">{directoryCheckText(result)}</p>
    </div>
  );
}

function MatchLine({ group, role }: { group: string; role: string }) {
  const name = firstValue(group);
  const label = roleLabel(role);

  return (
    <li title={group}>
      <Trans>
        {name} gives {label}
      </Trans>
    </li>
  );
}
