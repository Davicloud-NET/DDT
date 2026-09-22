// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";
import { useId } from "react";

import { AutosaveStatus } from "@/components/AutosaveStatus";
import { formatBytes, plural } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import type { SequenceView } from "@/sequences/sequences";

import type { PackageSummary } from "./packages";
import { TargetsEditor } from "./TargetsEditor";
import { usePackageRow } from "./usePackageRow";

import styles from "./PackageRow.module.scss";

export interface PackageRowProps {
  item: PackageSummary;
  // The sequences that give it to machines; null until they are read.
  users: SequenceView[] | null;
  // The registered machines its targets match.
  matches: number;
  canEdit: boolean;
  modelsListId: string;
  manufacturersListId: string;
  now: number;
  onDelete: () => void;
}

// One package, whose name and targets are edited in place and saved as they change.
export function PackageRow({
  item,
  users,
  matches,
  canEdit,
  modelsListId,
  manufacturersListId,
  now,
  onDelete,
}: PackageRowProps) {
  const row = usePackageRow(item);
  const nameMessagesId = useId();
  const nameMessages = row.messages("name");

  return (
    <tr>
      <td>
        <fieldset className={styles.cell} disabled={!canEdit}>
          <input
            type="text"
            className={styles.name}
            aria-label={`Name of ${item.name}`}
            value={row.edit.name}
            aria-invalid={nameMessages.length > 0 ? true : undefined}
            aria-describedby={nameMessages.length > 0 ? nameMessagesId : undefined}
            onChange={(event) => {
              row.change({ name: event.target.value });
            }}
          />
          {nameMessages.length > 0 && (
            <span id={nameMessagesId} className={styles.error}>
              {nameMessages.join(" ")}
            </span>
          )}
          <span className={styles.secondary}>{item.originalFileName ?? "Unknown file"}</span>
          <AutosaveStatus state={row.state} />
        </fieldset>
      </td>
      <td>{item.kind}</td>
      <td>
        <div>{`${plural(item.fileCount, "file")}, ${formatBytes(item.expandedBytes)} unpacked`}</div>
        <div className={styles.secondary}>{`${formatBytes(item.sizeBytes)} zip`}</div>
      </td>
      <td>
        {item.kind === "Drivers" ? (
          <fieldset className={styles.cell} disabled={!canEdit}>
            <TargetsEditor
              name={item.name}
              targets={row.edit.targets}
              messages={row.messages("targets")}
              modelsListId={modelsListId}
              manufacturersListId={manufacturersListId}
              onChange={(targets, immediate) => {
                row.change({ targets }, immediate);
              }}
            />
          </fieldset>
        ) : (
          <span className={styles.secondary}>
            Chosen by the Run script steps that name it, not by model.
          </span>
        )}
      </td>
      <td>
        {item.kind === "Drivers"
          ? matches === 0
            ? "No registered machine"
            : plural(matches, "machine")
          : null}
      </td>
      <td>
        {users === null ? null : users.length === 0 ? (
          <span className={styles.secondary}>No sequence</span>
        ) : (
          <ul className={styles.users}>
            {users.map((sequence) => (
              <li key={sequence.id}>
                <Link
                  to="/sequences/$sequenceId"
                  params={{ sequenceId: sequence.id }}
                  className={styles.link}
                >
                  {sequence.name}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </td>
      <td title={new Date(item.uploadedUtc).toLocaleString()}>
        <div>{relativeTime(item.uploadedUtc, now)}</div>
        {item.uploadedBy !== null && <div className={styles.secondary}>by {item.uploadedBy}</div>}
      </td>
      {canEdit && (
        <td>
          <button
            type="button"
            className={styles.delete}
            aria-label={`Delete ${item.name}`}
            onClick={onDelete}
          >
            Delete
          </button>
        </td>
      )}
    </tr>
  );
}
