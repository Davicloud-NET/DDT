// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";

import { packagesQuery, updatePackage, type PackageSummary } from "./packages";
import { cleanTargets } from "./packageTargets";
import { useTargetRows } from "./useTargetRows";
import { parseBuilds } from "./windowsPackageView";

// The package dialog's fields and its save, which replaces the package in the list with the server's answer.
export function usePackageForm(item: PackageSummary, onSaved: () => void) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(item.name);
  const [description, setDescription] = useState(item.description ?? "");
  const [bootImage, setBootImage] = useState(item.bootImage);
  const [buildsText, setBuildsText] = useState(item.windows?.builds.join(", ") ?? "");
  const targets = useTargetRows(item.targets);
  const drivers = item.kind === "Drivers";
  const windows = item.kind === "WindowsPackage";
  const cleaned = cleanTargets(targets.rows);
  const builds = parseBuilds(buildsText);

  const save = useMutation({
    mutationFn: () =>
      updatePackage(item.id, {
        name: name.trim(),
        description: description.trim() === "" ? null : description.trim(),
        targets: drivers ? cleaned : item.targets,
        ...(drivers ? { bootImage } : {}),
        ...(windows ? { builds } : {}),
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData(packagesQuery.queryKey, (list) =>
        list?.map((existing) => (existing.id === saved.id ? saved : existing)),
      );
      onSaved();
    },
  });

  const errors =
    save.error instanceof ApiError && save.error.status === 400
      ? (save.error.problem?.errors ?? {})
      : null;
  const fieldErrors = (field: string): string[] =>
    field === "builds" && builds === null
      ? [t`Enter numbers such as 26100.`]
      : (errors?.[field] ?? []);

  return {
    name,
    setName,
    description,
    setDescription,
    bootImage,
    setBootImage,
    buildsText,
    setBuildsText,
    // A build that is no number keeps the form from saving.
    canSave: name.trim() !== "" && (!windows || builds !== null),
    targets,
    cleaned,
    save,
    fieldErrors,
    fieldError: (field: string) => fieldErrors(field).join(" "),
    // A refusal that names no field can't be shown at a field, so the dialog shows it on its own.
    unplacedError:
      save.isError && (errors === null || Object.keys(errors).length === 0)
        ? save.error.message
        : null,
  };
}
