// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  createBundle,
  saveBundle,
  upsertBundle,
  type ApplicationBundleView,
  type SaveApplicationBundleRequest,
} from "../applications";
import { useEntryForm, type EntryForm } from "../drawer/useEntryForm";

// The fields a bundle's drawer edits.
export interface BundleEdit {
  name: string;
  description: string;
  members: string[];
}

export type BundleForm = EntryForm<ApplicationBundleView, BundleEdit>;

export function bundleEditOf(bundle: ApplicationBundleView | null): BundleEdit {
  return bundle === null
    ? { name: "", description: "", members: [] }
    : { name: bundle.name, description: bundle.description ?? "", members: bundle.members };
}

export function bundleRequestOf(revision: number, edit: BundleEdit): SaveApplicationBundleRequest {
  const description = edit.description.trim();

  return {
    revision,
    name: edit.name.trim(),
    description: description === "" ? null : description,
    members: edit.members,
  };
}

function send(base: ApplicationBundleView | null, revision: number, edit: BundleEdit) {
  const request = bundleRequestOf(revision, edit);

  return base === null ? createBundle(request) : saveBundle(base.id, request);
}

function isShown(field: string): boolean {
  return /^(name|description|members)(\.|\[|$)/.test(field);
}

export function useBundleForm(
  bundle: ApplicationBundleView | null,
  onClose: () => void,
): BundleForm {
  return useEntryForm({
    item: bundle,
    seed: bundleEditOf(bundle),
    editOf: bundleEditOf,
    save: send,
    put: upsertBundle,
    isShown: () => isShown,
    onClose,
  });
}
