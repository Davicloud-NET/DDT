// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { ImageUploadSession } from "@/images/images";
import { percentOf } from "@/lib/format";
import { Button } from "@/ui/Button";

// Uploads the server holds part of. The page cannot keep a file over a reload, so each resumes when chosen again.
export function UnfinishedUploads({
  sessions,
  onDiscard,
}: {
  sessions: readonly ImageUploadSession[];
  onDiscard: (session: ImageUploadSession) => void;
}) {
  const { t: translate } = useLingui();

  return (
    <div className="flex flex-col gap-1.5">
      <span className="type-label text-ink">
        <Trans>Unfinished uploads</Trans>
      </span>
      <ul className="flex flex-col">
        {sessions.map((session) => {
          const file = session.fileName;
          const percent = percentOf(session.offset, session.length);

          return (
            <li
              key={session.id}
              className="flex flex-wrap items-center gap-3 border-t border-line-soft py-2 first:border-t-0"
            >
              <span className="min-w-0 flex-1 type-small text-ink-2">
                <Trans>
                  Choose {file} again to resume at {percent}%.
                </Trans>
              </span>
              <Button
                size="sm"
                variant="quiet"
                aria-label={translate`Discard ${file}`}
                onPress={() => {
                  onDiscard(session);
                }}
              >
                <Trans>Discard</Trans>
              </Button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
