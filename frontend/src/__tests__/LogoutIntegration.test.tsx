import { render, screen, waitFor, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../context/AuthContext'
import { useAuth } from '../context/useAuth'
import { LoginForm } from '../components/LoginForm'
import { App } from '../App'
import type { LoginResponse } from '../types/auth'

const AuthStateInspector: React.FC = () => {
  const { authState } = useAuth()
  return (
    <div data-testid="auth-state-inspector">
      <span data-testid="auth-status">{authState.status}</span>
      <span data-testid="has-token">{authState.accessToken ? 'yes' : 'no'}</span>
      <span data-testid="has-expires">{authState.expiresAt ? 'yes' : 'no'}</span>
      <span data-testid="token-value">{authState.accessToken ?? ''}</span>
      <span data-testid="expires-value">{authState.expiresAt ?? ''}</span>
    </div>
  )
}

const SessionSeeder: React.FC<{ session: LoginResponse }> = ({ session }) => {
  const { setSession } = useAuth()
  return (
    <button type="button" onClick={() => setSession(session)}>
      Seed Session
    </button>
  )
}

interface TestTreeProps {
  initialSession?: LoginResponse
}

const ApplicationAuthTree: React.FC<TestTreeProps> = ({ initialSession }) => {
  return (
    <AuthProvider>
      <AuthStateInspector />
      {initialSession && <SessionSeeder session={initialSession} />}
      <LoginForm />
    </AuthProvider>
  )
}

describe('Logout Integration & Application Flow', () => {
  const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.integration.sample.access.token'
  const mockExpiresAt = '2026-10-03T23:59:59Z'

  const mockSession: LoginResponse = {
    accessToken: mockAccessToken,
    expiresAt: mockExpiresAt,
  }

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

  it('1. Authenticated App Logout Flow: executes POST /api/auth/logout with credentials, transitions UI to Sign In, wipes in-memory auth state, and leaves no storage or URL trace', async () => {
    const user = userEvent.setup()

    // 1. Mount application authentication tree with seeded session
    render(<ApplicationAuthTree initialSession={mockSession} />)

    // Verify initial unauthenticated state before seed
    expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')

    // Seed session to establish authenticated state
    await user.click(screen.getByRole('button', { name: /seed session/i }))

    // Verify Authenticated UI and state
    expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
    expect(screen.getByText('Session Established')).toBeInTheDocument()
    expect(screen.getByTestId('auth-status')).toHaveTextContent('authenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('yes')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('yes')
    expect(screen.getByTestId('token-value')).toHaveTextContent(mockAccessToken)
    expect(screen.getByTestId('expires-value')).toHaveTextContent(mockExpiresAt)

    // 2. Mock successful HTTP 204 No Content for logout API contract
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
    } as Response)

    // 3. Trigger Log Out
    const logoutBtn = screen.getByRole('button', { name: /log out/i })
    await user.click(logoutBtn)

    // 4. Verify API request contract: POST /api/auth/logout, credentials: 'include', no body
    await waitFor(() => {
      expect(fetch).toHaveBeenCalledTimes(1)
    })
    const [requestUrl, requestInit] = vi.mocked(fetch).mock.calls[0]
    expect(requestUrl).toContain('/api/auth/logout')
    expect(requestInit?.method).toBe('POST')
    expect(requestInit?.credentials).toBe('include')
    expect(requestInit?.body).toBeUndefined()

    // 5. Verify Authenticated UI transitions to Sign In
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })
    expect(screen.getByRole('button', { name: /sign in to vaultx/i })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /authenticated session/i })).toBeNull()

    // 6. Verify Access token and expiresAt are completely removed from React state
    expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('no')
    expect(screen.getByTestId('token-value')).toHaveTextContent('')
    expect(screen.getByTestId('expires-value')).toHaveTextContent('')

    // 7. Security Assertions: No token appears in localStorage, sessionStorage, or URL
    expect(localStorage.getItem('accessToken')).toBeNull()
    expect(localStorage.getItem('token')).toBeNull()
    expect(localStorage.length).toBe(0)

    expect(sessionStorage.getItem('accessToken')).toBeNull()
    expect(sessionStorage.getItem('token')).toBeNull()
    expect(sessionStorage.length).toBe(0)

    expect(window.location.search).not.toContain(mockAccessToken)
    expect(window.location.hash).not.toContain(mockAccessToken)
    expect(document.cookie).not.toContain('refreshToken')
  })

  it('1b. Full App Flow: user logs in through App, establishes session, logs out, and App transitions back to Sign In', async () => {
    const user = userEvent.setup()

    // Mock Login response
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        accessToken: mockAccessToken,
        expiresAt: mockExpiresAt,
      }),
    } as Response)

    render(<App />)

    // Complete login flow
    await user.type(screen.getByLabelText(/email address/i), 'integration.user@vaultx.local')
    await user.type(screen.getByLabelText(/^master password/i), 'SecurePass2026!')
    await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
    })

    // Mock Logout response
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
    } as Response)

    // Trigger logout
    await user.click(screen.getByRole('button', { name: /log out/i }))

    // App transitions back to Sign In view
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })
    expect(screen.queryByRole('region', { name: /authenticated session/i })).toBeNull()
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('2. Network Failure: clears local auth state, returns UI to Sign In, suppresses raw exception, and prevents token exposure', async () => {
    const user = userEvent.setup()

    render(<ApplicationAuthTree initialSession={mockSession} />)
    await user.click(screen.getByRole('button', { name: /seed session/i }))

    expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()

    // Simulate network error / fetch rejection
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError('Failed to fetch'))

    // Trigger Log Out
    const logoutBtn = screen.getByRole('button', { name: /log out/i })
    await user.click(logoutBtn)

    // Verify local auth state is still cleared
    await waitFor(() => {
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    })
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('no')
    expect(screen.getByTestId('token-value')).toHaveTextContent('')

    // Verify UI returns to Sign In
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /authenticated session/i })).toBeNull()

    // Verify raw exception is NOT rendered
    expect(screen.queryByText(/Failed to fetch/i)).toBeNull()
    expect(screen.queryByText(/TypeError/i)).toBeNull()
    expect(screen.queryByText(/stack/i)).toBeNull()

    // Verify token is not exposed in DOM or storage
    expect(document.body.textContent).not.toContain(mockAccessToken)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.search).not.toContain(mockAccessToken)
  })

  it('3. Loading & Duplicate Prevention: shows Signing Out... state, disables button, and prevents duplicate API calls while request is in flight', async () => {
    const user = userEvent.setup()

    render(<ApplicationAuthTree initialSession={mockSession} />)
    await user.click(screen.getByRole('button', { name: /seed session/i }))

    expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()

    // Create an unresolved pending promise for the logout request
    let resolveLogout: (value: Response) => void
    const pendingPromise = new Promise<Response>((resolve) => {
      resolveLogout = resolve
    })

    vi.mocked(fetch).mockImplementation(() => pendingPromise)

    const logoutBtn = screen.getByRole('button', { name: /log out/i })

    // First click triggers logout
    await user.click(logoutBtn)

    // Verify Signing Out... state and disabled attribute
    const pendingBtn = screen.getByRole('button', { name: /signing out\.\.\./i })
    expect(pendingBtn).toBeInTheDocument()
    expect(pendingBtn).toBeDisabled()
    expect(pendingBtn).toHaveAttribute('aria-busy', 'true')

    // Attempt repeated clicks while request is pending
    await user.click(pendingBtn)
    await user.click(pendingBtn)

    // Verify only a single API call was initiated
    expect(fetch).toHaveBeenCalledTimes(1)

    // Complete the logout request
    await act(async () => {
      resolveLogout({
        ok: true,
        status: 204,
      } as Response)
    })

    // Verify transition to Sign In is completed
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })
    expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
  })
})
