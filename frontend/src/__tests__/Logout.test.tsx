import { render, screen, waitFor, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../context/AuthContext'
import { useAuth } from '../context/useAuth'
import { LoginForm } from '../components/LoginForm'
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

const LogoutTrigger: React.FC = () => {
  const { logout } = useAuth()
  return (
    <button type="button" onClick={() => void logout().catch(() => undefined)}>
      Trigger Logout
    </button>
  )
}

const LogoutErrorCapture: React.FC<{ onError: (err: unknown) => void }> = ({ onError }) => {
  const { logout } = useAuth()
  return (
    <button
      type="button"
      onClick={() => {
        void logout().catch(onError)
      }}
    >
      Trigger Logout With Error Capture
    </button>
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

describe('AuthContext logout', () => {
  const mockSession: LoginResponse = {
    accessToken: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.logout.test.token',
    expiresAt: '2026-10-03T20:00:00Z',
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

  it('clears auth state on successful logout (status, accessToken, expiresAt)', async () => {
    const user = userEvent.setup()

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
    } as Response)

    render(
      <AuthProvider>
        <AuthStateInspector />
        <SessionSeeder session={mockSession} />
        <LogoutTrigger />
      </AuthProvider>
    )

    await user.click(screen.getByRole('button', { name: /seed session/i }))

    expect(screen.getByTestId('auth-status')).toHaveTextContent('authenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('yes')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('yes')

    await user.click(screen.getByRole('button', { name: /trigger logout/i }))

    await waitFor(() => {
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    })
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('no')
    expect(screen.getByTestId('token-value')).toHaveTextContent('')
    expect(screen.getByTestId('expires-value')).toHaveTextContent('')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = vi.mocked(fetch).mock.calls[0]
    expect(url).toContain('/api/auth/logout')
    expect(init?.method).toBe('POST')
    expect(init?.credentials).toBe('include')
    expect(init?.body).toBeUndefined()
  })

  it('clears auth state even when logout API fails (mandatory network-failure behavior)', async () => {
    const user = userEvent.setup()
    const capturedErrors: unknown[] = []

    vi.mocked(fetch).mockRejectedValueOnce(new Error('Network error'))

    render(
      <AuthProvider>
        <AuthStateInspector />
        <SessionSeeder session={mockSession} />
        <LogoutErrorCapture onError={(err) => capturedErrors.push(err)} />
      </AuthProvider>
    )

    await user.click(screen.getByRole('button', { name: /seed session/i }))

    expect(screen.getByTestId('auth-status')).toHaveTextContent('authenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('yes')

    await user.click(screen.getByRole('button', { name: /trigger logout with error capture/i }))

    await waitFor(() => {
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    })
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    expect(screen.getByTestId('has-expires')).toHaveTextContent('no')
    expect(screen.getByTestId('token-value')).toHaveTextContent('')

    // API error is rethrown after local cleanup (local vs server logout distinction)
    await waitFor(() => {
      expect(capturedErrors.length).toBe(1)
    })
  })

  it('does not write tokens to localStorage, sessionStorage, or URL on logout', async () => {
    const user = userEvent.setup()

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
    } as Response)

    render(
      <AuthProvider>
        <AuthStateInspector />
        <SessionSeeder session={mockSession} />
        <LogoutTrigger />
      </AuthProvider>
    )

    await user.click(screen.getByRole('button', { name: /seed session/i }))
    await user.click(screen.getByRole('button', { name: /trigger logout/i }))

    await waitFor(() => {
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    })

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.search).not.toContain(mockSession.accessToken)
    expect(window.location.hash).not.toContain(mockSession.accessToken)
    expect(document.cookie).not.toContain('refreshToken')
  })
})

describe('LoginForm Logout control', () => {
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

  const loginThenGetLogoutButton = async () => {
    const user = userEvent.setup()
    const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.logout.ui.token'

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        accessToken: mockAccessToken,
        expiresAt: '2026-10-03T20:00:00Z',
      }),
    } as Response)

    render(
      <AuthProvider>
        <AuthStateInspector />
        <LoginForm />
      </AuthProvider>
    )

    await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
    await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
    })

    return { user, mockAccessToken }
  }

  it('renders Log Out control for authenticated state', async () => {
    await loginThenGetLogoutButton()
    expect(screen.getByRole('button', { name: /log out/i })).toBeInTheDocument()
  })

  it('invokes logout API and transitions to unauthenticated UI on success', async () => {
    const { user, mockAccessToken } = await loginThenGetLogoutButton()

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
    } as Response)

    await user.click(screen.getByRole('button', { name: /log out/i }))

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })

    expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    expect(screen.queryByRole('region', { name: /authenticated session/i })).toBeNull()

    const logoutCall = vi.mocked(fetch).mock.calls.find(([url]) => String(url).includes('/api/auth/logout'))
    expect(logoutCall).toBeDefined()
    expect(logoutCall?.[1]?.method).toBe('POST')
    expect(logoutCall?.[1]?.credentials).toBe('include')
    expect(logoutCall?.[1]?.body).toBeUndefined()

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.search).not.toContain(mockAccessToken)
    expect(document.cookie).not.toContain('refreshToken')
  })

  it('clears local auth state and returns to login UI when logout API fails', async () => {
    const { user, mockAccessToken } = await loginThenGetLogoutButton()

    vi.mocked(fetch).mockRejectedValueOnce(new Error('Network error'))

    await user.click(screen.getByRole('button', { name: /log out/i }))

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })

    expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
    expect(screen.getByTestId('has-token')).toHaveTextContent('no')

    // Safe error handling: no stack traces, tokens, or server internals rendered
    expect(screen.queryByText(/network error/i)).toBeNull()
    expect(screen.queryByText(mockAccessToken)).toBeNull()
    expect(screen.queryByText(/stack/i)).toBeNull()
    expect(document.body.textContent).not.toContain(mockAccessToken)
  })

  it('disables logout button and shows loading state while logout is in flight', async () => {
    const { user } = await loginThenGetLogoutButton()

    let resolveLogout: (value: unknown) => void
    const pendingLogout = new Promise((resolve) => {
      resolveLogout = resolve
    })

    vi.mocked(fetch).mockImplementationOnce(() => pendingLogout as Promise<Response>)

    await user.click(screen.getByRole('button', { name: /log out/i }))

    expect(screen.getByRole('button', { name: /signing out\.\.\./i })).toBeDisabled()

    await act(async () => {
      resolveLogout!({
        ok: true,
        status: 204,
      })
    })

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })
  })

  it('prevents duplicate logout requests while a logout is already running', async () => {
    const { user } = await loginThenGetLogoutButton()

    let resolveLogout: (value: unknown) => void
    const pendingLogout = new Promise((resolve) => {
      resolveLogout = resolve
    })

    vi.mocked(fetch).mockImplementation(() => pendingLogout as Promise<Response>)

    const logoutBtn = screen.getByRole('button', { name: /log out/i })
    await user.click(logoutBtn)
    await user.click(logoutBtn)
    await user.click(logoutBtn)

    const logoutCalls = vi.mocked(fetch).mock.calls.filter(([url]) => String(url).includes('/api/auth/logout'))
    expect(logoutCalls).toHaveLength(1)

    await act(async () => {
      resolveLogout!({
        ok: true,
        status: 204,
      })
    })

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    })
  })
})
