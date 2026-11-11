// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { bootImageQuery } from "@/boot/bootImage";
import { defaultKeymap, keymapName } from "@/boot/linuxBoot";

import { SettingSelect } from "../parts/SettingSelect";
import { SettingsGroup } from "../parts/SettingsGroup";

import type { DeploymentForm } from "./deploymentSettings";

// The language names stay untranslated, since each language is named in its own language. The keyboard layouts are the
// ones the server's Linux boot environment has keymaps for, with the one that is set although it has none.
export function ConsoleLanguageGroup({
  form,
  canChange,
}: {
  form: DeploymentForm;
  canChange: boolean;
}) {
  const keymaps = useQuery(bootImageQuery).data?.linux?.keymaps ?? [];
  const set = form.values?.consoleKeyboardLayout ?? null;
  const layouts = [...new Set([...keymaps, ...(set === null ? [] : [set])])].filter(
    (keymap) => keymap !== defaultKeymap,
  );

  return (
    <SettingsGroup title={<Trans>The console at the machine</Trans>}>
      <SettingSelect
        form={form}
        field="consoleLanguage"
        canChange={canChange}
        label={<Trans>Language</Trans>}
        empty={t`The language of Windows PE`}
        options={[
          { id: "en", label: "English" },
          { id: "de", label: "Deutsch" },
        ]}
      />
      <SettingSelect
        form={form}
        field="consoleKeyboardLayout"
        canChange={canChange}
        label={<Trans>Keyboard layout in the Linux boot environment</Trans>}
        empty={keymapName(defaultKeymap)}
        options={layouts.map((keymap) => ({ id: keymap, label: keymapName(keymap) }))}
      />
    </SettingsGroup>
  );
}
