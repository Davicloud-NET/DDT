// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { IconTrash } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { Tooltip } from "@/ui/Tooltip";

export function DeleteKey({ name, onPress }: { name: string; onPress: () => void }) {
  return (
    <Tooltip content={<Trans>Delete</Trans>}>
      <AriaButton
        aria-label={t`Delete ${name}`}
        onPress={onPress}
        className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-fail-text focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconTrash size={17} stroke={2} />
      </AriaButton>
    </Tooltip>
  );
}
