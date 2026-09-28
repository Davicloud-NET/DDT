// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ComboBox, ListBoxItem } from "@/ui/Select";

// The server name used to build the suggested URLs for an HTTP boot file. Only the URLs are stored.
export function BootUrlServerField({
  server,
  onChange,
  options,
  editable,
}: {
  server: string;
  onChange: (server: string) => void;
  options: string[];
  editable: boolean;
}) {
  return (
    <ComboBox
      label={<Trans>Server name for the URL</Trans>}
      hint={
        <Trans>
          The name or IPv4 address machines reach DDT by. The boot file offers URLs with it.
        </Trans>
      }
      mono
      allowsCustomValue
      inputValue={server}
      onInputChange={onChange}
      isReadOnly={!editable}
    >
      {options.map((option) => (
        <ListBoxItem key={option} id={option} textValue={option}>
          {option}
        </ListBoxItem>
      ))}
    </ComboBox>
  );
}
