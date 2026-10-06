import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'

describe('Login Integration & End-to-End User Flow', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    localStorage.clear()
    sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  const installFetchRoutes = (loginResponse: Response | Error) => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const url = String(input)
      if (url.endsWith('/api/auth/refresh')) {
        return { ok: false, status: 401, json: async () => ({}) } as Response
      }
      if (url.endsWith('/api/auth/login')) {
        if (loginResponse instanceof Error) {
          throw loginResponse
        }
        return loginResponse
      }
      if (url.endsWith('/api/vault')) {
        return { ok: false, status: 404, json: async () => ({}) } as Response
      }
      throw new Error(`Unexpected request: ${url}`)
    })
  }

  it('1. End-to-End: user logs in via real API contract, establishes in-memory session, and displays authenticated UI', async () => {
    const user = userEvent.setup()
    const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e2e.sample.access.token'
    const validEmail = 'e2e.user@vaultx.local'
    const validPassword = 'SecurePass@VaultX2026!'

    // Mock real HTTP backend response according to API contract
    installFetchRoutes({
      ok: true,
      status: 200,
      json: async () => ({
        accessToken: mockAccessToken,
        expiresAt: '2026-10-01T23:59:59Z',
      }),
    } as Response)

    render(<App />)

    // 1. Verify Login UI is active by default
    await screen.findByRole('heading', { name: /sign in/i })

    // 2. Fill in valid credentials
    const emailInput = screen.getByLabelText(/email address/i)
    const passwordInput = screen.getByLabelText(/^master password/i)
    const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })

    await user.type(emailInput, validEmail)
    await user.type(passwordInput, validPassword)

    // 3. Submit Login
    await user.click(submitBtn)

    // 4. Verify API request was made with exact contract and credentials: 'include'
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([url]) =>
      String(url).endsWith('/api/auth/login'))).toBe(true))
    const [requestUrl, requestInit] = vi.mocked(fetch).mock.calls.find(([url]) =>
      String(url).endsWith('/api/auth/login'))!
    expect(requestUrl).toContain('/api/auth/login')
    expect(requestInit?.method).toBe('POST')
    expect(requestInit?.credentials).toBe('include')

    const requestBody = JSON.parse(requestInit?.body as string)
    expect(requestBody).toEqual({
      email: validEmail,
      password: validPassword,
    })

    // 5. Verify Authenticated UI is rendered
    await waitFor(() => {
      expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
    })
    expect(screen.getByRole('heading', { name: 'Your Vault' })).toBeInTheDocument()

    // 6. Security Assertions: Access token is in-memory only; NOT in persistent storage or URL
    expect(localStorage.getItem('accessToken')).toBeNull()
    expect(localStorage.getItem('token')).toBeNull()
    expect(localStorage.length).toBe(0)

    expect(sessionStorage.getItem('accessToken')).toBeNull()
    expect(sessionStorage.getItem('token')).toBeNull()
    expect(sessionStorage.length).toBe(0)

    expect(window.location.search).not.toContain(mockAccessToken)
    expect(window.location.hash).not.toContain(mockAccessToken)

    // 7. Security Assertion: Refresh token is not accessible via JavaScript document.cookie
    expect(document.cookie).not.toContain('refreshToken')
  })

  it('2. End-to-End: invalid credentials return 401, display generic error, and keep user unauthenticated', async () => {
    const user = userEvent.setup()

    installFetchRoutes({
      ok: false,
      status: 401,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
        title: 'Unauthorized',
        status: 401,
        detail: 'Invalid email or password.',
      }),
    } as Response)

    render(<App />)

    const emailInput = await screen.findByLabelText(/email address/i)
    const passwordInput = screen.getByLabelText(/^master password/i)
    const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })

    await user.type(emailInput, 'wrong.credentials@vaultx.local')
    await user.type(passwordInput, 'IncorrectPassword123!')
    await user.click(submitBtn)

    // Verify generic error is presented
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Invalid email or password.')
    })

    // Verify session was NOT established
    expect(screen.queryByRole('region', { name: /authenticated session/i })).toBeNull()

    // Verify storage remains empty
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('3. End-to-End: seamless navigation between Registration view and Login view', async () => {
    const user = userEvent.setup()
    installFetchRoutes(new Error('No login request expected'))
    render(<App />)

    const nav = await screen.findByRole('navigation', { name: /authentication navigation/i })

    // Click "Create Account" in navigation tab
    const registerTab = within(nav).getByRole('button', { name: /create account/i })
    await user.click(registerTab)

    // Verify Registration form is rendered
    expect(screen.getByRole('heading', { name: /create account/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/confirm master password/i)).toBeInTheDocument()

    // Click "Sign In" in navigation tab
    const signInTab = within(nav).getByRole('button', { name: /sign in/i })
    await user.click(signInTab)

    // Verify Login form is rendered
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.queryByLabelText(/confirm master password/i)).toBeNull()
  })

  it('4. End-to-End: handles network disconnection gracefully with safe message', async () => {
    const user = userEvent.setup()

    installFetchRoutes(new Error('Network error'))

    render(<App />)

    await user.type(await screen.findByLabelText(/email address/i), 'user@vaultx.local')
    await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
    await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Unable to connect to the server. Please check your connection and try again.')
    })

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })
})
