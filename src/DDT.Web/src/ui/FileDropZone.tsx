// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconUpload } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { DropZone, FileTrigger, Text, type FileDropItem } from "react-aria-components";

import { Button } from "./Button";
import { cx } from "./cx";

// A well to drop one file on, with a key that chooses it instead.
export function FileDropZone({
  label,
  title,
  hint,
  accept,
  onFile,
  className,
}: {
  // The drop target's name for screen readers.
  label: string;
  title: ReactNode;
  hint: ReactNode;
  accept: readonly string[];
  onFile: (file: File) => void;
  className?: string;
}) {
  return (
    <DropZone
      aria-label={label}
      onDrop={(event) => {
        const item = event.items.find(
          (candidate): candidate is FileDropItem => candidate.kind === "file",
        );

        if (item !== undefined) {
          void item.getFile().then(onFile);
        }
      }}
      className={({ isDropTarget }) =>
        cx(
          "flex flex-wrap items-center gap-x-4 gap-y-2 rounded-key bg-well px-4 py-3.5 shadow-[inset_0_0_0_1px_var(--color-line)] outline-none",
          isDropTarget && "shadow-[inset_0_0_0_2px_var(--color-focus)]",
          className,
        )
      }
    >
      <IconUpload aria-hidden="true" size={22} stroke={1.75} className="text-muted" />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <Text slot="label" className="type-label text-ink">
          {title}
        </Text>
        <span className="type-small text-muted">{hint}</span>
      </span>
      <FileTrigger
        acceptedFileTypes={[...accept]}
        onSelect={(list) => {
          const file = list === null ? undefined : Array.from(list)[0];

          if (file !== undefined) {
            onFile(file);
          }
        }}
      >
        <Button>
          <Trans>Choose a file</Trans>
        </Button>
      </FileTrigger>
    </DropZone>
  );
}
