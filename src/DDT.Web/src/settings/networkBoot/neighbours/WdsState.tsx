// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

import type { NetbootNeighbours, WdsChange } from "./neighbours";

// What WDS does on this computer right now, with the changes that fit. Without the helper DDT can change nothing
// there, so the notice stands alone.
export function WdsState({
  neighbours,
  isBusy,
  onChoose,
}: {
  neighbours: NetbootNeighbours;
  isBusy: boolean;
  onChoose: (change: WdsChange) => void;
}) {
  const choice = (change: WdsChange, label: ReactNode) => (
    <Button
      size="sm"
      isDisabled={isBusy}
      onPress={() => {
        onChoose(change);
      }}
    >
      {label}
    </Button>
  );

  if (!neighbours.wds.running) {
    return (
      <Notice
        actions={neighbours.helper ? choice("restore", <Trans>Start WDS again</Trans>) : null}
      >
        <Trans>
          Windows Deployment Services is installed on this computer and stopped, so DDT answers
          netboot here. Before WDS starts again, switch off ProxyDHCP and TFTP above.
        </Trans>
      </Notice>
    );
  }

  return (
    <Notice
      tone="attention"
      actions={
        neighbours.helper ? (
          <>
            {choice("boot-image", <Trans>Add DDT to the WDS boot menu</Trans>)}
            {choice("replace", <Trans>Use DDT in place of WDS</Trans>)}
          </>
        ) : null
      }
    >
      <Trans>
        Windows Deployment Services runs on this computer and holds UDP 69 and 4011, so machines
        that netboot reach WDS and not DDT. In its boot menu DDT sits next to MDT's LiteTouch and
        needs neither ProxyDHCP nor TFTP of its own.
      </Trans>
    </Notice>
  );
}
