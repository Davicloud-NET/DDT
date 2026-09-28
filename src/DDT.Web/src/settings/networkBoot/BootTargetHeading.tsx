// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

import { architectureDescription } from "../networkBoot";

export function BootTargetHeading({
  headingId,
  targetKey,
  editable,
  onRemove,
}: {
  headingId: string;
  targetKey: string;
  editable: boolean;
  onRemove: () => void;
}) {
  const { i18n, t } = useLingui();
  const key = targetKey;
  const description = architectureDescription(key);

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
      <h4 id={headingId} className="type-data text-ink">
        {key}
      </h4>
      {description === null ? null : (
        <span className="type-small text-muted">{i18n._(description)}</span>
      )}
      <span className="flex-1" />
      {editable ? (
        <Button
          size="sm"
          variant="quiet"
          aria-label={t`Remove the boot target for ${key}`}
          onPress={() => {
            onRemove();
          }}
        >
          <Trans>Remove</Trans>
        </Button>
      ) : null}
    </div>
  );
}
