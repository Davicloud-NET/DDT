// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import { Notice } from "@/ui/Notice";
import { SearchField } from "@/ui/SearchField";
import { Skeleton } from "@/ui/Skeleton";
import { useDebouncedValue } from "@/ui/useDebouncedValue";

import { findGroups, type DirectoryGroup } from "./users";

// How long typing rests before the directory is asked, so a group name is not searched letter by letter.
const SEARCH_DELAY_MS = 300;

// Finds directory groups by name, as the server's saved directory settings see them. The caller draws each group's
// row, with what it offers for the group.
export function DirectoryGroupSearch({
  heading: Heading,
  className,
  note,
  idle,
  unavailable,
  renderGroup,
}: {
  // The heading's level where the search sits.
  heading: "h3" | "h4";
  // The search field's width.
  className: string;
  // Under the field, such as that unsaved changes do not count yet.
  note?: ReactNode;
  // What shows before anything is typed.
  idle?: ReactNode;
  // Shown in place of the search while the directory cannot be searched.
  unavailable?: ReactNode;
  // A found group's row, an li with the group's distinguished name as its key.
  renderGroup: (group: DirectoryGroup) => ReactNode;
}) {
  const { t } = useLingui();
  const [query, setQuery] = useState("");
  const asked = useDebouncedValue(query, SEARCH_DELAY_MS).trim();

  const groups = useQuery({
    queryKey: ["directory", "groups", asked],
    queryFn: () => findGroups(asked),
    enabled: unavailable === undefined && asked !== "",
    retry: false,
    staleTime: 60_000,
  });

  if (unavailable !== undefined) {
    return unavailable;
  }

  return (
    <section className="flex flex-col gap-3">
      <Heading className="type-label text-ink">
        <Trans>Find a group</Trans>
      </Heading>
      <SearchField
        label={t`Find a directory group`}
        placeholder={t`Group name`}
        value={query}
        onChange={setQuery}
        className={className}
      />
      {note}
      {asked === "" ? (
        idle
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
          {groups.data.map(renderGroup)}
        </ul>
      )}
    </section>
  );
}
