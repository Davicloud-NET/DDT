// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Form } from "react-aria-components";

import type { HardwareModelCount } from "@/machines/machines";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { Switch } from "@/ui/Switch";
import { TextField } from "@/ui/TextField";

import { HardwareModelsField } from "./HardwareModelsField";
import type { PackageSummary } from "./packages";
import { usePackageForm } from "./usePackageForm";

// Changes a package's name and description, and for drivers the hardware models they are for.
export function PackageDialog({
  item,
  models,
  onClose,
}: {
  item: PackageSummary;
  models: readonly HardwareModelCount[];
  onClose: () => void;
}) {
  const form = usePackageForm(item, onClose);
  const { save } = form;
  const drivers = item.kind === "Drivers";
  const formId = `package-${item.id}`;

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Change the package</Trans>}
      isBusy={save.isPending}
      width="lg"
      footer={
        <>
          <Button variant="secondary" isDisabled={save.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={save.isPending || form.name.trim() === ""}
          >
            <Trans>Save package</Trans>
          </Button>
        </>
      }
    >
      {/* With the browser's own validation, a field the server marked invalid would block the next save until the
          dialog closed, so the form only tells assistive technology. */}
      <Form
        id={formId}
        className="flex flex-col gap-4"
        validationBehavior="aria"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate();
        }}
      >
        <TextField
          label={<Trans>Name</Trans>}
          value={form.name}
          onChange={form.setName}
          isRequired
          isInvalid={form.fieldError("name") !== ""}
          errorMessage={form.fieldError("name")}
          autoFocus
        />
        <TextField
          label={<Trans>Description</Trans>}
          hint={<Trans>Optional. What is in it and where it came from.</Trans>}
          value={form.description}
          onChange={form.setDescription}
          multiline
          rows={3}
          isInvalid={form.fieldError("description") !== ""}
          errorMessage={form.fieldError("description")}
        />

        {drivers ? (
          <HardwareModelsField
            targets={form.targets}
            cleaned={form.cleaned}
            models={models}
            errors={form.fieldErrors("targets")}
          />
        ) : null}

        {drivers ? (
          <BootImageSwitch isSelected={form.bootImage} onChange={form.setBootImage} />
        ) : null}

        {form.unplacedError !== null ? <Notice tone="fail">{form.unplacedError}</Notice> : null}
      </Form>
    </Dialog>
  );
}

function BootImageSwitch({
  isSelected,
  onChange,
}: {
  isSelected: boolean;
  onChange: (selected: boolean) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <Switch isSelected={isSelected} onChange={onChange}>
        <Trans>Add to the Windows PE boot image</Trans>
      </Switch>
      <span className="type-small text-muted">
        <Trans>
          For network and storage drivers a machine needs before the agent runs, such as those of
          new laptops. They reach machines with the next boot image build; Boot, Boot image says
          when the build is older than the drivers.
        </Trans>
      </span>
    </div>
  );
}
