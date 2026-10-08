// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { bootImageQuery } from "@/boot/bootImage";
import { linuxBootFile } from "@/boot/linuxBoot";

import type { PxeForm } from "../networkBoot";
import { SettingSelect } from "../parts/SettingSelect";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import { settingsText } from "../settings";

// Which boot environment a machine without a run starts, and what the Linux one's kernel is told in addition.
export function BootEnvironmentGroup({ form }: { form: PxeForm }) {
  const file = useQuery(bootImageQuery).data?.linux?.bootFile ?? linuxBootFile;
  const warnings = (form.view?.warnings ?? []).filter(
    (warning) => warning.field === "defaultBootEnvironment",
  );

  return (
    <SettingsGroup title={<Trans>Windows PE or Linux</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          A machine with a run starts the boot environment its task sequence needs: Windows PE for
          Windows, the Linux boot environment for a Linux image. This setting decides for every
          other 64-bit UEFI machine.
        </Trans>
      </p>
      <SettingSelect
        form={form}
        field="defaultBootEnvironment"
        canChange
        className="max-w-[36rem]"
        label={<Trans>Machines without a run start</Trans>}
        hint={
          <Trans>
            Machines that start the Linux boot environment load {file} over TFTP, whatever the boot
            target for 64-bit UEFI names.
          </Trans>
        }
        options={[
          {
            id: "Auto",
            label: t`Automatic: Windows PE once a boot image is built, Linux until then`,
          },
          { id: "WindowsPE", label: t`Windows PE` },
          { id: "Linux", label: t`The Linux boot environment` },
        ]}
      />
      {warnings.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {warnings.map((warning, index) => (
            <li key={index} className="type-small text-attention-text">
              {settingsText(warning)}
            </li>
          ))}
        </ul>
      ) : null}
      <p className="type-small text-ink-2">
        <Trans>
          A DHCP server that names a boot file itself starts the same environment on every machine,
          and if it names another server's file, the Linux boot environment does not start at all.
        </Trans>
      </p>
      <SettingText
        form={form}
        field="linuxKernelArguments"
        canChange
        mono
        label={<Trans>Extra kernel arguments for Linux</Trans>}
        hint={
          <Trans>
            Words added to the kernel command line of the Linux boot environment, such as
            console=ttyS0,115200 for a serial console. Only letters, digits and _ . : , = + / @ -
            are taken.
          </Trans>
        }
      />
    </SettingsGroup>
  );
}
