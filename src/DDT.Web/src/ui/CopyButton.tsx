// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState, type ReactNode } from "react";

import { Button, type ButtonProps } from "./Button";

// A button that copies text to the clipboard and says "Copied" once it has.
export function CopyButton({
  text,
  children,
  onCopy,
  ...props
}: Omit<ButtonProps, "onPress" | "children"> & {
  text: string;
  // The button's label until it has copied, such as "Copy command".
  children: ReactNode;
  // Called with whether the browser allowed the copy. Without it, a refusal ends up in the browser's console.
  onCopy?: (copied: boolean) => void;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      onCopy?.(true);
    } catch (error) {
      // The clipboard is only there on HTTPS and localhost, and a browser may refuse it.
      if (onCopy === undefined) {
        throw error;
      }

      setCopied(false);
      onCopy(false);
    }
  }

  return (
    <Button {...props} onPress={() => void copy()}>
      {copied ? <Trans>Copied</Trans> : children}
    </Button>
  );
}
