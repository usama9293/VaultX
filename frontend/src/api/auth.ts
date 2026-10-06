import type { ApiErrorResponse, LoginRequest, LoginResponse, RegisterUserRequest, UserResponse } from '../types/auth'

export class ApiError extends Error {
  public readonly status: number
  public readonly errorResponse?: ApiErrorResponse

  constructor(message: string, status: number, errorResponse?: ApiErrorResponse) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.errorResponse = errorResponse
  }
}

export async function registerUser(request: RegisterUserRequest): Promise<UserResponse> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  const endpoint = `${baseUrl}/api/auth/register`

  let response: Response
  try {
    response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        email: request.email,
        password: request.password,
        confirmPassword: request.confirmPassword,
      }),
    })
  } catch {
    throw new ApiError('Unable to connect to the server. Please check your connection and try again.', 0)
  }

  if (response.ok) {
    return (await response.json()) as UserResponse
  }

  let errorData: ApiErrorResponse | undefined
  try {
    errorData = (await response.json()) as ApiErrorResponse
  } catch {
    // Ignore JSON parse failure on non-JSON response
  }

  if (response.status === 409) {
    const detail = errorData?.detail || 'A user with this email already exists.'
    throw new ApiError(detail, 409, errorData)
  }

  if (response.status === 400) {
    const title = errorData?.title || 'One or more validation errors occurred.'
    throw new ApiError(title, 400, errorData)
  }

  // Safe fallback for 500 or any unexpected status code
  const serverDetail = errorData?.detail || 'An unexpected error occurred while processing your request. Please try again later.'
  throw new ApiError(serverDetail, response.status, errorData)
}

export async function loginUser(request: LoginRequest): Promise<LoginResponse> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  const endpoint = `${baseUrl}/api/auth/login`

  let response: Response
  try {
    response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: JSON.stringify({
        email: request.email,
        password: request.password,
      }),
    })
  } catch {
    throw new ApiError('Unable to connect to the server. Please check your connection and try again.', 0)
  }

  if (response.ok) {
    return (await response.json()) as LoginResponse
  }

  let errorData: ApiErrorResponse | undefined
  try {
    errorData = (await response.json()) as ApiErrorResponse
  } catch {
    // Ignore JSON parse failure on non-JSON response
  }

  if (response.status === 401) {
    const detail = errorData?.detail || 'Invalid email or password.'
    throw new ApiError(detail, 401, errorData)
  }

  if (response.status === 400) {
    const title = errorData?.title || 'One or more validation errors occurred.'
    throw new ApiError(title, 400, errorData)
  }

  // Safe generic fallback for 500 or any unexpected status code
  const serverDetail = 'Unable to sign in right now. Please try again.'
  throw new ApiError(serverDetail, response.status, errorData)
}

/**
 * Requests server-side session revocation via POST /api/auth/logout.
 *
 * The browser attaches the HttpOnly refreshToken cookie automatically when
 * credentials: 'include' is set. The client must never read or send the
 * refresh token from JavaScript.
 *
 * Success: HTTP 204 No Content (empty body).
 */
export async function logoutUser(): Promise<void> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  const endpoint = `${baseUrl}/api/auth/logout`

  let response: Response
  try {
    response = await fetch(endpoint, {
      method: 'POST',
      credentials: 'include',
    })
  } catch {
    throw new ApiError('Unable to connect to the server. Please check your connection and try again.', 0)
  }

  // Backend contract: HTTP 204 No Content with empty body
  if (response.status === 204 || response.ok) {
    return
  }

  let errorData: ApiErrorResponse | undefined
  try {
    errorData = (await response.json()) as ApiErrorResponse
  } catch {
    // Ignore JSON parse failure on non-JSON / empty response
  }

  // Safe generic fallback — never expose server internals
  throw new ApiError('Unable to complete sign-out right now. Please try again.', response.status, errorData)
}

let refreshSessionRequest: Promise<LoginResponse | null> | null = null

export function refreshSession(): Promise<LoginResponse | null> {
  if (refreshSessionRequest !== null) {
    return refreshSessionRequest
  }

  refreshSessionRequest = refreshSessionCore().finally(() => {
    refreshSessionRequest = null
  })
  return refreshSessionRequest
}

async function refreshSessionCore(): Promise<LoginResponse | null> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  let response: Response
  try {
    response = await fetch(`${baseUrl}/api/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
    })
  } catch {
    throw new ApiError('Unable to restore your session. Check your connection and try again.', 0)
  }

  if (response.status === 401) {
    return null
  }

  if (!response.ok) {
    throw new ApiError('Unable to restore your session. Please try again.', response.status)
  }

  try {
    return (await response.json()) as LoginResponse
  } catch {
    throw new ApiError('The server returned an invalid session response.', response.status)
  }
}
