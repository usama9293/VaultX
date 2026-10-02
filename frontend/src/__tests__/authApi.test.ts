import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, loginUser, registerUser } from '../api/auth'
import type { LoginRequest, RegisterUserRequest } from '../types/auth'

describe('auth API client - registerUser', () => {
  const mockRequest: RegisterUserRequest = {
    email: 'test@example.com',
    password: 'ValidPassword123!',
    confirmPassword: 'ValidPassword123!',
  }

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('sends POST /api/auth/register with correct headers and payload', async () => {
    const mockSuccessResponse = {
      id: 'd9b9c0e2-632b-426b-8857-4187e59bbf62',
      email: 'test@example.com',
      createdAt: '2026-09-29T12:00:00Z',
      updatedAt: '2026-09-29T12:00:00Z',
    }

    const fetchMock = vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => mockSuccessResponse,
    } as Response)

    const result = await registerUser(mockRequest)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toContain('/api/auth/register')
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' })

    const parsedBody = JSON.parse(init?.body as string)
    expect(parsedBody).toEqual({
      email: 'test@example.com',
      password: 'ValidPassword123!',
      confirmPassword: 'ValidPassword123!',
    })

    expect(result).toEqual(mockSuccessResponse)
  })

  it('throws ApiError with 409 status on duplicate email conflict', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 409,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
        title: 'Conflict',
        status: 409,
        detail: 'A user with this email already exists.',
      }),
    } as Response)

    await expect(registerUser(mockRequest)).rejects.toThrow('A user with this email already exists.')
  })

  it('throws ApiError with 400 status on backend validation failure', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 400,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Email: ['Email format is invalid.'],
        },
      }),
    } as Response)

    await expect(registerUser(mockRequest)).rejects.toThrow('One or more validation errors occurred.')
  })

  it('throws ApiError with safe message on unexpected 500 error', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
        title: 'An error occurred while processing your request.',
        status: 500,
        detail: 'An unexpected error occurred.',
      }),
    } as Response)

    await expect(registerUser(mockRequest)).rejects.toThrow('An unexpected error occurred.')
  })

  it('throws ApiError with status 0 on network disconnect', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new Error('Failed to fetch'))

    const promise = registerUser(mockRequest)
    await expect(promise).rejects.toThrow('Unable to connect to the server.')
    await expect(promise).rejects.toBeInstanceOf(ApiError)
  })
})

describe('auth API client - loginUser', () => {
  const mockLoginRequest: LoginRequest = {
    email: 'user@example.com',
    password: 'MasterPassword123!',
  }

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('sends POST /api/auth/login with credentials: include and JSON payload', async () => {
    const mockSuccessResponse = {
      accessToken: 'header.payload.signature',
      expiresAt: '2026-10-01T18:00:00Z',
    }

    const fetchMock = vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockSuccessResponse,
    } as Response)

    const result = await loginUser(mockLoginRequest)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toContain('/api/auth/login')
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' })
    expect(init?.credentials).toBe('include')

    const parsedBody = JSON.parse(init?.body as string)
    expect(parsedBody).toEqual({
      email: 'user@example.com',
      password: 'MasterPassword123!',
    })

    expect(result).toEqual(mockSuccessResponse)
  })

  it('throws ApiError with generic message on HTTP 401 Unauthorized', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 401,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
        title: 'Unauthorized',
        status: 401,
        detail: 'Invalid email or password.',
      }),
    } as Response)

    await expect(loginUser(mockLoginRequest)).rejects.toThrow('Invalid email or password.')
  })

  it('throws ApiError on HTTP 400 validation error', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 400,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Email: ['Email format is invalid.'],
        },
      }),
    } as Response)

    await expect(loginUser(mockLoginRequest)).rejects.toThrow('One or more validation errors occurred.')
  })

  it('throws ApiError with safe generic message on unexpected HTTP 500 error', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
        title: 'Internal Server Error',
        status: 500,
        detail: 'Database connection failed with sensitive sql details',
      }),
    } as Response)

    // Must not expose internal server/sql details
    await expect(loginUser(mockLoginRequest)).rejects.toThrow('Unable to sign in right now. Please try again.')
  })

  it('throws ApiError with status 0 on network disconnect', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new Error('Failed to fetch'))

    const promise = loginUser(mockLoginRequest)
    await expect(promise).rejects.toThrow('Unable to connect to the server.')
    await expect(promise).rejects.toBeInstanceOf(ApiError)
  })
})

