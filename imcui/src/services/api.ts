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
  ) {
    super(`API Error ${status}: ${statusText}`);
    this.name = 'ApiError';
  }
}

async function request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
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

  if (!response.ok) {
    let data: unknown;
    try {
      data = await response.json();
    } catch {
      data = response;
    }

    throw new ApiError(response.status, response.statusText, data);
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
