const API_BASE_URL = import.meta.env.QCLI_API_URL;
if (!API_BASE_URL) {
  throw new Error('QCLI_API_URL is not configured');
}
// const API_BASE_URL = 'https://your-api.com/api';

const token = localStorage.getItem('access_token');

export class ApiError extends Error {
  constructor(
    public status: number,
    public statusText: string,
    public data?: unknown,
    message?: string,
  ) {
    super(message ?? `API Error ${status}: ${statusText}`);
    this.name = 'ApiError';
  }
}

/** Pulls the human-readable part out of an ASP.NET problem-details style payload. */
function serverMessage(data: unknown): string | undefined {
  if (!data || typeof data !== 'object') return undefined;
  const record = data as Record<string, unknown>;
  for (const key of ['detail', 'message', 'title', 'error']) {
    const value = record[key];
    if (typeof value === 'string' && value.trim()) return value;
    if (Array.isArray(value) && value.length) return value.map(String).join('; ');
  }
  return undefined;
}

export function getErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return String(error);
}

async function request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token
        ? {
            Authorization: `Bearer ${token}`,
          }
        : {}),
      ...options.headers,
    },
  });
  } catch {
    // fetch only rejects on network/CORS failures, which are otherwise invisible in the UI.
    throw new ApiError(0, 'Network error', undefined, `Cannot reach the API at ${API_BASE_URL}`);
  }

  if (!response.ok) {
    let data: unknown;
    try {
      data = await response.json();
    } catch {
      data = undefined;
    }

    throw new ApiError(response.status, response.statusText, data, serverMessage(data));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const api = {
  get<T>(endpoint: string, options?: RequestInit): Promise<T> {
    return request<T>(endpoint, {
      ...options,
      method: 'GET',
    });
  },

  post<T>(endpoint: string, body?: unknown, options?: RequestInit): Promise<T> {
    const requestOptions: RequestInit = {
      ...options,
      method: 'POST',
    };

    if (body !== undefined) {
      requestOptions.body = JSON.stringify(body);
    }

    return request<T>(endpoint, requestOptions);
  },

  put<T>(endpoint: string, body?: unknown, options?: RequestInit): Promise<T> {
    const requestOptions: RequestInit = {
      ...options,
      method: 'PUT',
    };

    if (body !== undefined) {
      requestOptions.body = JSON.stringify(body);
    }

    return request<T>(endpoint, requestOptions);
  },

  delete<T>(endpoint: string, options?: RequestInit): Promise<T> {
    return request<T>(endpoint, {
      ...options,
      method: 'DELETE',
    });
  },
};
