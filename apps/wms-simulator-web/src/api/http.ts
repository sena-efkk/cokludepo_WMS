const API_BASE = import.meta.env.VITE_WMS_API_URL ?? '';

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    const text = await response.text();
    let message = text;
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed === 'string') {
        message = parsed;
      } else if (parsed?.message) {
        message = parsed.message;
      } else if (parsed?.rejectionReason) {
        message = `${parsed.rejectionCode ?? 'REJECTED'}: ${parsed.rejectionReason}`;
      } else if (parsed?.staleReason) {
        message = `SOURCING_STALE: ${parsed.staleReason}`;
      }
    } catch {
      // text olarak kalır
    }
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export function post<T>(path: string, body: unknown): Promise<T> {
  return api<T>(path, { method: 'POST', body: JSON.stringify(body) });
}

export function get<T>(path: string): Promise<T> {
  return api<T>(path);
}

export function newRequestId(): string {
  return crypto.randomUUID();
}
