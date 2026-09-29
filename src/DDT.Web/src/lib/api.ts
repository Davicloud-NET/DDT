// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import {
  isServerMessage,
  serverText,
  type ServerArguments,
  type ServerMessage,
} from "@/lib/serverText";

const CSRF_HEADER = "X-CSRF-TOKEN";
const SAFE_METHODS = ["GET", "HEAD", "OPTIONS", "TRACE"];

let csrfToken: string | null = null;

async function ensureCsrfToken(): Promise<string | null> {
  if (csrfToken !== null) {
    return csrfToken;
  }

  const response = await fetch("/api/auth/session", { credentials: "same-origin" });
  csrfToken = response.headers.get(CSRF_HEADER);

  return csrfToken;
}

// The server binds antiforgery tokens to the signed in identity and hands back a fresh one
// whenever the identity changes, so every response is checked for a replacement.
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const method = init.method ?? "GET";
  const headers = new Headers(init.headers);

  if (!SAFE_METHODS.includes(method)) {
    const token = await ensureCsrfToken();

    if (token !== null) {
      headers.set(CSRF_HEADER, token);
    }
  }

  const response = await fetch(path, { ...init, headers, credentials: "same-origin" });
  const refreshed = response.headers.get(CSRF_HEADER);

  if (refreshed !== null) {
    csrfToken = refreshed;
  }

  return response;
}

export async function apiGet<TResponse>(path: string): Promise<TResponse> {
  const response = await apiFetch(path);

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as TResponse;
}

export async function apiPost<TResponse>(path: string, body?: unknown): Promise<TResponse> {
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return readBody<TResponse>(response);
}

// keepalive lets a save outlive the page, for the flush when it is hidden. Browsers cap such bodies at
// about 64 KB.
export async function apiPut<TResponse>(
  path: string,
  body: unknown,
  init: { keepalive?: boolean } = {},
): Promise<TResponse> {
  const response = await apiFetch(path, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    keepalive: init.keepalive ?? false,
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return readBody<TResponse>(response);
}

// Changes some fields of a resource and answers with all of it.
export async function apiPatchJson<TResponse>(path: string, body: unknown): Promise<TResponse> {
  const response = await apiFetch(path, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return readBody<TResponse>(response);
}

// Some deletions answer 204, others the changed resource, for example the machine whose deployment ended.
export async function apiDelete<TResponse = void>(path: string): Promise<TResponse> {
  const response = await apiFetch(path, { method: "DELETE" });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return readBody<TResponse>(response);
}

// Sends raw bytes and hands back the response whatever its status: the upload protocol reads its headers,
// such as Upload-Offset and Retry-After, on success and on refusal alike.
export function apiPatch(
  path: string,
  body: Blob,
  init: { headers?: HeadersInit; signal?: AbortSignal } = {},
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/octet-stream");

  return apiFetch(path, { method: "PATCH", headers, body, signal: init.signal ?? null });
}

// The server's problem details. errors holds a validation failure's messages by field, such as "mac". code, args
// and errorCodes are the title and those messages as codes. apiErrorFrom translates them, so a page can show title
// and errors as they are.
export interface ApiProblem {
  title?: string;
  errors?: Record<string, string[]>;
  code?: string;
  args?: ServerArguments;
  errorCodes?: Record<string, (ServerMessage | null)[]>;
}

export class ApiError extends Error {
  public readonly status: number;
  public readonly problem: ApiProblem | null;

  public constructor(status: number, message: string, problem: ApiProblem | null = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

export async function apiErrorFrom(response: Response): Promise<ApiError> {
  const problem = translated(await readProblem(response));

  return new ApiError(response.status, errorMessage(response, problem), problem);
}

// Translates the title and the field errors if the server sent their codes. Everything else stays as sent.
function translated(problem: ApiProblem | null): ApiProblem | null {
  if (problem === null) {
    return null;
  }

  const { title, errors, errorCodes } = problem;

  return {
    ...problem,
    ...(typeof title === "string" ? { title: serverText(problem.code, problem.args, title) } : {}),
    ...(errors === undefined
      ? {}
      : {
          errors: Object.fromEntries(
            Object.entries(errors).map(([field, messages]) => [
              field,
              messages.map((english, index) => {
                const coded = errorCodes?.[field]?.[index];

                return isServerMessage(coded)
                  ? serverText(coded.code, coded.args, english)
                  : english;
              }),
            ]),
          ),
        }),
  };
}

async function readBody<TResponse>(response: Response): Promise<TResponse> {
  if (response.status === 204 || response.headers.get("Content-Length") === "0") {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}

async function readProblem(response: Response): Promise<ApiProblem | null> {
  try {
    const body: unknown = await response.json();

    return typeof body === "object" && body !== null ? body : null;
  } catch {
    return null;
  }
}

function errorMessage(response: Response, problem: ApiProblem | null): string {
  const status = String(response.status);
  const fallback =
    response.statusText === ""
      ? t`The server answered with status ${status}.`
      : response.statusText;

  return Object.values(problem?.errors ?? {})[0]?.[0] ?? problem?.title ?? fallback;
}
