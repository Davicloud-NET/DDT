// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { TextSetting } from "../../fields/TextSetting";
import type { InputPartProps } from "./declarationFields";

// The longest answer a Text input takes. A number out of range is ignored, so the last valid one stays.
export function MaxLengthSetting({ input, at, findings, update }: InputPartProps) {
  return (
    <TextSetting
      label={<Trans>At most this many characters</Trans>}
      field={at("maxLength")}
      findings={findings}
      hint={<Trans>Empty for no limit.</Trans>}
      mono
      value={input.maxLength === null ? "" : String(input.maxLength)}
      onChange={(text) => {
        const number = Number(text);

        if (text.trim() === "") {
          update({ maxLength: null });
        } else if (Number.isInteger(number) && number > 0 && number <= 1024) {
          update({ maxLength: number });
        }
      }}
    />
  );
}
