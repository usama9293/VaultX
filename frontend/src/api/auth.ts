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

