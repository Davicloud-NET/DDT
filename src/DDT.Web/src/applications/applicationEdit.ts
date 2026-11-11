// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  DEFAULT_TIMEOUT_MINUTES,
  type ApplicationDetection,
  type ApplicationFiles,
  type ApplicationKind,
  type ApplicationUpload,
  type ApplicationView,
  type SaveApplicationRequest,
} from "./applications";

export type DetectionKind = "none" | ApplicationDetection["kind"];

// Every detection's text as typed, so switching between them loses nothing.
export interface DetectionEdit {
  kind: DetectionKind;
  productCode: string;
  keyName: string;
  path: string;
  minimumVersion: string;
  familyName: string;
}

// The fields an application's drawer edits. Fields of the other kinds are kept but not sent.
export interface ApplicationEdit {
  name: string;
  description: string;
  publisher: string;
  version: string;
  kind: ApplicationKind;
  files: ApplicationFiles | null;
  command: string;
  successExitCodes: number[];
  restartExitCodes: number[];
  detection: DetectionEdit;
  dependsOn: string[];
  wingetId: string;
  wingetVersion: string;
  timeoutMinutes: number;
  continueOnFailure: boolean;
}

const noDetection: DetectionEdit = {
  kind: "none",
  productCode: "",
  keyName: "",
  path: "",
  minimumVersion: "",
  familyName: "",
};

export function detectionEditOf(detection: ApplicationDetection | null): DetectionEdit {
  switch (detection?.kind) {
    case "productCode":
      return { ...noDetection, kind: "productCode", productCode: detection.productCode };
    case "uninstallKey":
      return { ...noDetection, kind: "uninstallKey", keyName: detection.keyName };
    case "file":
      return {
        ...noDetection,
        kind: "file",
        path: detection.path,
        minimumVersion: detection.minimumVersion ?? "",
      };
    case "packageFamily":
      return { ...noDetection, kind: "packageFamily", familyName: detection.familyName };
    default:
      return noDetection;
  }
}

// A new application of a kind, with the codes a Windows installer usually answers.
export function newApplicationEdit(kind: ApplicationKind): ApplicationEdit {
  return {
    name: "",
    description: "",
    publisher: "",
    version: "",
    kind,
    files: null,
    command: "",
    successExitCodes: [0],
    restartExitCodes: kind === "Command" ? [3010] : [],
    detection: noDetection,
    dependsOn: [],
    wingetId: "",
    wingetVersion: "",
    timeoutMinutes: DEFAULT_TIMEOUT_MINUTES,
    continueOnFailure: false,
  };
}

// A new application from its uploaded files, filled from what the server read in them. Without a suggestion it is a
// command's files.
export function uploadedApplicationEdit(upload: ApplicationUpload): ApplicationEdit {
  const suggestion = upload.suggestion ?? null;
  const edit = { ...newApplicationEdit(suggestion?.kind ?? "Command"), files: upload.files };

  if (suggestion === null) {
    return edit;
  }

  return {
    ...edit,
    name: suggestion.name ?? "",
    publisher: suggestion.publisher ?? "",
    version: suggestion.version ?? "",
    command: suggestion.command ?? "",
    successExitCodes: suggestion.successExitCodes,
    restartExitCodes: suggestion.restartExitCodes,
    detection: detectionEditOf(suggestion.detection),
  };
}

export function applicationEditOf(application: ApplicationView): ApplicationEdit {
  return {
    name: application.name,
    description: application.description ?? "",
    publisher: application.publisher ?? "",
    version: application.version ?? "",
    kind: application.kind,
    files: application.files,
    command: application.command ?? "",
    successExitCodes: application.successExitCodes,
    restartExitCodes: application.restartExitCodes,
    detection: detectionEditOf(application.detection),
    dependsOn: application.dependsOn,
    wingetId: application.wingetId ?? "",
    wingetVersion: application.wingetVersion ?? "",
    timeoutMinutes: application.timeoutMinutes,
    continueOnFailure: application.continueOnFailure,
  };
}

function optional(value: string): string | null {
  const trimmed = value.trim();

  return trimmed === "" ? null : trimmed;
}

export function detectionOf(edit: DetectionEdit): ApplicationDetection | null {
  switch (edit.kind) {
    case "productCode":
      return { kind: "productCode", productCode: edit.productCode.trim() };
    case "uninstallKey":
      return { kind: "uninstallKey", keyName: edit.keyName.trim() };
    case "file":
      return {
        kind: "file",
        path: edit.path.trim(),
        minimumVersion: optional(edit.minimumVersion),
      };
    case "packageFamily":
      return { kind: "packageFamily", familyName: edit.familyName.trim() };
    case "none":
      return null;
  }
}

// Only what the kind uses is sent, so a field typed before switching the kind is not refused.
export function applicationRequestOf(
  revision: number,
  edit: ApplicationEdit,
): SaveApplicationRequest {
  const { kind } = edit;
  const winget = kind === "Winget";

  return {
    revision,
    name: edit.name.trim(),
    description: optional(edit.description),
    publisher: optional(edit.publisher),
    version: optional(edit.version),
    kind,
    packageId: winget ? null : (edit.files?.packageId ?? null),
    command: kind === "Command" ? optional(edit.command) : null,
    successExitCodes: edit.successExitCodes,
    restartExitCodes: edit.restartExitCodes,
    detection: detectionOf(edit.detection),
    dependsOn: edit.dependsOn,
    wingetId: winget ? optional(edit.wingetId) : null,
    wingetVersion: winget ? optional(edit.wingetVersion) : null,
    timeoutMinutes: edit.timeoutMinutes,
    continueOnFailure: edit.continueOnFailure,
  };
}

// The fields the drawer shows for a kind, as the server names them in its problems.
export function isShownFor(kind: ApplicationKind): (field: string) => boolean {
  const shown = [
    "name",
    "description",
    "publisher",
    "version",
    "kind",
    "successExitCodes",
    "restartExitCodes",
    "detection",
    "dependsOn",
    "timeoutMinutes",
    ...(kind === "Command" ? ["command"] : []),
    ...(kind === "Winget" ? ["wingetId", "wingetVersion"] : ["packageId"]),
  ];

  return (field) => shown.some((name) => new RegExp(`^${name}(\\.|\\[|$)`).test(field));
}
