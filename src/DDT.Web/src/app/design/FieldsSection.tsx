// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Checkbox } from "@/ui/Checkbox";
import { NumberField } from "@/ui/NumberField";
import { SearchField } from "@/ui/SearchField";
import { SecretValue } from "@/ui/SecretValue";
import { ComboBox, ListBoxItem, Select } from "@/ui/Select";
import { Switch } from "@/ui/Switch";
import { TextField } from "@/ui/TextField";

import { DesignSection } from "./DesignSection";

export function FieldsSection() {
  return (
    <DesignSection title="Fields">
      <SearchField label="Find a machine" placeholder="Name, MAC, serial or address" />
      <TextField
        label="Computer name"
        hint="Up to 15 letters, digits and hyphens."
        mono
        defaultValue="LAB-PC-016"
      />
      <TextField
        label="Computer name"
        mono
        defaultValue="LAB-PC-016!"
        isInvalid
        errorMessage='Use letters, digits and hyphens only. Remove the "!".'
      />
      <Select
        label="Task sequence"
        placeholder="Choose a sequence"
        hint="Chosen by the rule for Dell Latitude 7450."
      >
        <ListBoxItem id="w11">Windows 11 24H2 with Office</ListBoxItem>
        <ListBoxItem id="ubuntu" description="Writes a raw disk image">
          Ubuntu 24.04 LTS
        </ListBoxItem>
        <ListBoxItem id="broken" isDisabled>
          Kiosk (2 problems)
        </ListBoxItem>
      </Select>
      <ComboBox label="Model" placeholder="Type to narrow the list">
        <ListBoxItem id="7450">Dell Latitude 7450</ListBoxItem>
        <ListBoxItem id="t14">Lenovo ThinkPad T14 Gen 5</ListBoxItem>
        <ListBoxItem id="840">HP EliteBook 840 G10</ListBoxItem>
      </ComboBox>
      <NumberField label="Timeout" hint="Minutes." defaultValue={60} minValue={1} />
      <SecretValue label="One-time password" value="Tq8v-Rk3m-Wz6p-Hd2n" />
      <Checkbox defaultSelected>Restart after this step</Checkbox>
      <Switch defaultSelected>Require web approval</Switch>
    </DesignSection>
  );
}
