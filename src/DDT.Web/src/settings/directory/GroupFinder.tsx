// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { DirectoryGroupSearch } from "@/users/DirectoryGroupSearch";

import { FoundGroup } from "./FoundGroup";

// Groups found by name, to add to the map. The server searches the directory as the section is saved.
export function GroupFinder({
  ready,
  unsaved,
  map,
  onAdd,
}: {
  ready: boolean;
  unsaved: boolean;
  map: Record<string, string>;
  onAdd: (group: string, name: string | null) => void;
}) {
  return (
    <DirectoryGroupSearch
      heading="h4"
      className="w-full max-w-120"
      unavailable={
        ready ? undefined : (
          <p className="type-small text-muted">
            <Trans>
              Once directory sign-in is on and its server and search base are saved, you can find
              groups by name here. Until then, add a group by its distinguished name.
            </Trans>
          </p>
        )
      }
      note={
        unsaved ? (
          <p className="type-small text-attention-text">
            <Trans>
              The search asks the directory as it is saved, not with the changes above that are not
              saved yet.
            </Trans>
          </p>
        ) : undefined
      }
      renderGroup={(group) => (
        <FoundGroup key={group.distinguishedName} group={group} map={map} onAdd={onAdd} />
      )}
    />
  );
}
