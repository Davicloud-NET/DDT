// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { DirectoryGroupSearch } from "@/users/DirectoryGroupSearch";

import { FoundGroup } from "./FoundGroup";

// Finds groups by name to add to the map. The server searches the directory with the saved settings.
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
              To find groups by name, turn directory sign-in on and save its server and search base.
            </Trans>
          </p>
        )
      }
      note={
        unsaved ? (
          <p className="type-small text-attention-text">
            <Trans>The search uses the saved settings, not your unsaved changes.</Trans>
          </p>
        ) : undefined
      }
      renderGroup={(group) => (
        <FoundGroup key={group.distinguishedName} group={group} map={map} onAdd={onAdd} />
      )}
    />
  );
}
