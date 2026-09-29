// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconArrowDown, IconArrowUp } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { iconKey } from "./declarationFields";

// Moves a variable or an input up or down its list.
export function MoveKeys({
  name,
  index,
  count,
  onMove,
}: {
  name: string;
  index: number;
  count: number;
  onMove: (to: number) => void;
}) {
  const { t } = useLingui();

  return (
    <>
      <AriaButton
        aria-label={t`Move ${name} up`}
        isDisabled={index === 0}
        className={iconKey}
        onPress={() => {
          onMove(index - 1);
        }}
      >
        <IconArrowUp size={14} stroke={2} />
      </AriaButton>
      <AriaButton
        aria-label={t`Move ${name} down`}
        isDisabled={index === count - 1}
        className={iconKey}
        onPress={() => {
          onMove(index + 1);
        }}
      >
        <IconArrowDown size={14} stroke={2} />
      </AriaButton>
    </>
  );
}
