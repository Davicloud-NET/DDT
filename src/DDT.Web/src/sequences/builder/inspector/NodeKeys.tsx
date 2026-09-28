// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconBoxMultiple, IconTrash } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { buttonClass } from "@/ui/buttonClass";

// The keys that wrap the node in a group, unwrap a container and remove the node.
export function NodeKeys({
  name,
  container,
  onWrap,
  onUnwrap,
  onRemove,
}: {
  name: string;
  container: boolean;
  onWrap: () => void;
  onUnwrap: () => void;
  onRemove: () => void;
}) {
  const { t } = useLingui();

  return (
    <div className="flex flex-wrap items-center gap-2 border-t border-line-soft pt-4">
      <AriaButton
        aria-label={t`Wrap ${name} in a group`}
        className={buttonClass("secondary", "sm")}
        onPress={onWrap}
      >
        <IconBoxMultiple aria-hidden="true" size={16} stroke={2} />
        <Trans>Wrap in a group</Trans>
      </AriaButton>
      {container ? (
        <AriaButton
          aria-label={t`Unwrap ${name}`}
          className={buttonClass("quiet", "sm")}
          onPress={onUnwrap}
        >
          <Trans>Unwrap</Trans>
        </AriaButton>
      ) : null}
      <AriaButton
        aria-label={t`Remove ${name}`}
        onPress={onRemove}
        className={buttonClass("quiet", "sm", "text-fail-text hover:text-fail-text")}
      >
        <IconTrash aria-hidden="true" size={16} stroke={2} />
        <Trans>Remove</Trans>
      </AriaButton>
    </div>
  );
}
