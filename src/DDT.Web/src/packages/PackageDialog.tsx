// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button as AriaButton, Form } from "react-aria-components";

import { ApiError } from "@/lib/api";
import type { HardwareModel, HardwareModelCount } from "@/machines/machines";
import { Button } from "@/ui/Button";
import { Switch } from "@/ui/Checkbox";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { ComboBox, ListBoxItem } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { matchingMachines, packagesQuery, updatePackage, type PackageSummary } from "./packages";

interface TargetRow {
  key: number;
  manufacturer: string;
  model: string;
}

// Changes a package's name and description, and for drivers the hardware models they are for. A save replaces
// the package in the list with the server's answer.
export function PackageDialog({
  item,
  models,
  onClose,
}: {
  item: PackageSummary;
  models: readonly HardwareModelCount[];
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [name, setName] = useState(item.name);
  const [description, setDescription] = useState(item.description ?? "");
  const [bootImage, setBootImage] = useState(item.bootImage);
  const [targets, setTargets] = useState<TargetRow[]>(() =>
    item.targets.map((target, index) => ({
      key: index,
      manufacturer: target.manufacturer ?? "",
      model: target.model,
    })),
  );
  const [nextKey, setNextKey] = useState(item.targets.length);
  const drivers = item.kind === "Drivers";

  const cleaned: HardwareModel[] = targets
    .filter((target) => target.model.trim() !== "")
    .map((target) => ({
      manufacturer: target.manufacturer.trim() === "" ? null : target.manufacturer.trim(),
      model: target.model.trim(),
    }));

  const save = useMutation({
    mutationFn: () =>
      updatePackage(item.id, {
        name: name.trim(),
        description: description.trim() === "" ? null : description.trim(),
        targets: drivers ? cleaned : item.targets,
        ...(drivers ? { bootImage } : {}),
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData(packagesQuery.queryKey, (list) =>
        list?.map((existing) => (existing.id === saved.id ? saved : existing)),
      );
      onClose();
    },
  });

  const errors =
    save.error instanceof ApiError && save.error.status === 400
      ? (save.error.problem?.errors ?? {})
      : null;
  const fieldError = (field: string) => errors?.[field]?.join(" ") ?? "";
  const unplaced = save.isError && (errors === null || Object.keys(errors).length === 0);
  const manufacturers = [
    ...new Set(
      models.flatMap((model) => (model.manufacturer === null ? [] : [model.manufacturer])),
    ),
  ];
  const matching = matchingMachines(cleaned, models);
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
            isDisabled={save.isPending || name.trim() === ""}
          >
            <Trans>Save package</Trans>
          </Button>
        </>
      }
    >
      {/* The server's refusal marks a field invalid; with the browser's own validation that would block the next
          save until the dialog closed, so the form only tells assistive technology. */}
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
          value={name}
          onChange={setName}
          isRequired
          isInvalid={fieldError("name") !== ""}
          errorMessage={fieldError("name")}
          autoFocus
        />
        <TextField
          label={<Trans>Description</Trans>}
          hint={<Trans>Optional. What is in it and where it came from.</Trans>}
          value={description}
          onChange={setDescription}
          multiline
          rows={3}
          isInvalid={fieldError("description") !== ""}
          errorMessage={fieldError("description")}
        />

        {drivers ? (
          <fieldset className="flex flex-col gap-2.5">
            <legend className="type-label text-ink">
              <Trans>For these hardware models</Trans>
            </legend>
            <p className="type-small text-muted">
              <Trans>
                An Inject drivers step adds these drivers to machines whose model matches one of
                them. End a model with * to match every model that starts with it; leave the
                manufacturer empty for any.
              </Trans>
            </p>
            {targets.map((target, index) => {
              const position = index + 1;

              return (
                <div key={target.key} className="grid grid-cols-[1fr_1.4fr_auto] items-end gap-2">
                  <ComboBox
                    label={<Trans>Manufacturer</Trans>}
                    allowsCustomValue
                    inputValue={target.manufacturer}
                    onInputChange={(manufacturer) => {
                      setTargets((rows) =>
                        rows.map((row) =>
                          row.key === target.key ? { ...row, manufacturer } : row,
                        ),
                      );
                    }}
                  >
                    {manufacturers.map((manufacturer) => (
                      <ListBoxItem key={manufacturer} id={manufacturer} textValue={manufacturer}>
                        {manufacturer}
                      </ListBoxItem>
                    ))}
                  </ComboBox>
                  <ComboBox
                    label={<Trans>Model</Trans>}
                    allowsCustomValue
                    inputValue={target.model}
                    onInputChange={(model) => {
                      setTargets((rows) =>
                        rows.map((row) => (row.key === target.key ? { ...row, model } : row)),
                      );
                    }}
                  >
                    {models
                      .filter(
                        (model) =>
                          target.manufacturer.trim() === "" ||
                          model.manufacturer === target.manufacturer.trim(),
                      )
                      .map((model) => (
                        <ListBoxItem
                          key={`${model.manufacturer ?? ""}/${model.model}`}
                          id={`${model.manufacturer ?? ""}/${model.model}`}
                          textValue={model.model}
                        >
                          {model.model}
                        </ListBoxItem>
                      ))}
                  </ComboBox>
                  <AriaButton
                    aria-label={t`Remove model ${position}`}
                    onPress={() => {
                      setTargets((rows) => rows.filter((row) => row.key !== target.key));
                    }}
                    className="mb-0.5 flex size-9 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
                  >
                    <IconX size={16} stroke={2} />
                  </AriaButton>
                </div>
              );
            })}
            <span className="flex flex-wrap items-center gap-3">
              <Button
                size="sm"
                onPress={() => {
                  setTargets((rows) => [...rows, { key: nextKey, manufacturer: "", model: "" }]);
                  setNextKey((key) => key + 1);
                }}
              >
                <IconPlus size={14} stroke={2} aria-hidden="true" />
                <Trans>Add a model</Trans>
              </Button>
              <span className="type-small text-muted">
                {cleaned.length === 0
                  ? t`No model yet: no machine gets these drivers.`
                  : matching === 0
                    ? t`No registered machine matches yet.`
                    : plural(matching, {
                        one: "Matches # registered machine.",
                        other: "Matches # registered machines.",
                      })}
              </span>
            </span>
            {fieldError("targets") !== "" ? (
              <span className="type-small text-fail-text">{fieldError("targets")}</span>
            ) : null}
          </fieldset>
        ) : null}

        {drivers ? (
          <div className="flex flex-col gap-1">
            <Switch isSelected={bootImage} onChange={setBootImage}>
              <Trans>Add to the Windows PE boot image</Trans>
            </Switch>
            <span className="type-small text-muted">
              <Trans>
                For network and storage drivers a machine needs before the agent runs, such as those
                of new laptops. They reach machines with the next boot image build; Boot, Boot image
                says when the build is older than the drivers.
              </Trans>
            </span>
          </div>
        ) : null}

        {unplaced ? <Notice tone="fail">{save.error.message}</Notice> : null}
      </Form>
    </Dialog>
  );
}
