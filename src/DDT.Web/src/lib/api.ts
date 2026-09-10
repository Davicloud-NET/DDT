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

export async function apiPost<TResponse>(path: string, body?: unknown): Promise<TResponse> {
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  if (response.status === 204 || response.headers.get("Content-Length") === "0") {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}

export class ApiError extends Error {
  public readonly status: number;

  public constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as {
      title?: string;
      errors?: Record<string, string[]>;
    };

    if (problem.errors) {
      const first = Object.values(problem.errors)[0];

      if (first && first.length > 0) {
        return first[0] ?? response.statusText;
      }
    }

    return problem.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}
