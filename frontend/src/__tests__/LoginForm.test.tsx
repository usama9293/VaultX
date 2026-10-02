import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../context/AuthContext'
import { useAuth } from '../context/useAuth'
import { LoginForm } from '../components/LoginForm'

// Helper component to observe in-memory AuthState in tests
const AuthStateInspector: React.FC = () => {
  const { authState } = useAuth()
  return (
    <div data-testid="auth-state-inspector">
      <span data-testid="auth-status">{authState.status}</span>
      <span data-testid="has-token">{authState.accessToken ? 'yes' : 'no'}</span>
    </div>
  )
}

const renderLoginForm = (props?: { onNavigateToRegister?: () => void }) => {
  return render(
    <AuthProvider>
      <AuthStateInspector />
      <LoginForm {...props} />
    </AuthProvider>
  )
}

describe('LoginForm Component (Integration & Security)', () => {
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

  describe('1. Login UI Rendering', () => {
    it('renders login form header, email field, password field, and submit button', () => {
      renderLoginForm()

      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
      expect(screen.getByLabelText(/email address/i)).toBeInTheDocument()
      expect(screen.getByLabelText(/^master password/i)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /sign in to vaultx/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /show master password/i })).toBeInTheDocument()
    })

    it('toggles password visibility between password and text', async () => {
      const user = userEvent.setup()
      renderLoginForm()

      const passwordInput = screen.getByLabelText(/^master password/i)
      const toggleBtn = screen.getByRole('button', { name: /show master password/i })

      expect(passwordInput).toHaveAttribute('type', 'password')

      await user.click(toggleBtn)
      expect(passwordInput).toHaveAttribute('type', 'text')
      expect(screen.getByRole('button', { name: /hide master password/i })).toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: /hide master password/i }))
      expect(passwordInput).toHaveAttribute('type', 'password')
    })
  })

  describe('2. Client-Side Validation', () => {
    it('empty submission triggers required field validation and prevents API call', async () => {
      const user = userEvent.setup()
      renderLoginForm()

      const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })
      await user.click(submitBtn)

      expect(screen.getByText('Email is required.')).toBeInTheDocument()
      expect(screen.getByText('Password is required.')).toBeInTheDocument()
      expect(fetch).not.toHaveBeenCalled()
    })

    it('rejects invalid email formats and prevents API call', async () => {
      const user = userEvent.setup()
      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'not-an-email')
      await user.type(screen.getByLabelText(/^master password/i), 'SomePassword123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      expect(screen.getByText('Email format is invalid.')).toBeInTheDocument()
      expect(fetch).not.toHaveBeenCalled()
    })

    it('requires password when email is provided', async () => {
      const user = userEvent.setup()
      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      expect(screen.getByText('Password is required.')).toBeInTheDocument()
      expect(screen.queryByText('Email is required.')).not.toBeInTheDocument()
      expect(fetch).not.toHaveBeenCalled()
    })
  })

  describe('3. Loading & Submission Behavior', () => {
    it('displays loading state and disables submit button during request', async () => {
      const user = userEvent.setup()

      let resolvePromise: (value: unknown) => void
      const pendingResponse = new Promise((resolve) => {
        resolvePromise = resolve
      })

      vi.mocked(fetch).mockImplementationOnce(() => pendingResponse as Promise<Response>)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')

      const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })
      await user.click(submitBtn)

      // Button should be in loading state
      expect(screen.getByRole('button', { name: /signing in\.\.\./i })).toBeDisabled()

      // Resolve the fetch to clean up
      resolvePromise!({
        ok: true,
        status: 200,
        json: async () => ({ accessToken: 'token.123', expiresAt: '2026-10-01T20:00:00Z' }),
      })

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })
    })

    it('prevents duplicate submissions while request is in flight', async () => {
      const user = userEvent.setup()

      let resolvePromise: (value: unknown) => void
      const pendingResponse = new Promise((resolve) => {
        resolvePromise = resolve
      })

      vi.mocked(fetch).mockImplementation(() => pendingResponse as Promise<Response>)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')

      const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })
      await user.click(submitBtn)
      await user.click(submitBtn)
      await user.click(submitBtn)

      // Exactly 1 fetch call should have occurred
      expect(fetch).toHaveBeenCalledTimes(1)

      resolvePromise!({
        ok: true,
        status: 200,
        json: async () => ({ accessToken: 'token.123', expiresAt: '2026-10-01T20:00:00Z' }),
      })

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })
    })
  })

  describe('4. Successful Login Flow', () => {
    it('calls login API, stores access token in memory, sets auth status, and renders authenticated view', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.valid.token'

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      // Initial state is unauthenticated
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
      expect(screen.getByTestId('has-token')).toHaveTextContent('no')

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'ValidMasterPassword123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      // Verify API was called
      expect(fetch).toHaveBeenCalledTimes(1)
      const [url, init] = vi.mocked(fetch).mock.calls[0]
      expect(url).toContain('/api/auth/login')
      expect(init?.credentials).toBe('include')

      // Verify authenticated UI appears
      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })
      expect(screen.getByText('Session Established')).toBeInTheDocument()

      // Verify in-memory state is authenticated and holds the token
      expect(screen.getByTestId('auth-status')).toHaveTextContent('authenticated')
      expect(screen.getByTestId('has-token')).toHaveTextContent('yes')
    })
  })

  describe('5. Failed Login Flow', () => {
    it('displays generic error on HTTP 401 Unauthorized without storing token', async () => {
      const user = userEvent.setup()

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

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'nonexistent@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'WrongPassword123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('alert')).toHaveTextContent('Invalid email or password.')
      })

      // State remains unauthenticated with no token stored
      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
      expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    })

    it('displays safe connection error message on network failure', async () => {
      const user = userEvent.setup()

      vi.mocked(fetch).mockRejectedValueOnce(new Error('Network error'))

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('alert')).toHaveTextContent('Unable to connect to the server.')
      })

      expect(screen.getByTestId('auth-status')).toHaveTextContent('unauthenticated')
      expect(screen.getByTestId('has-token')).toHaveTextContent('no')
    })
  })

  describe('6. Security & Storage Boundaries', () => {
    it('does NOT store access token in localStorage', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive.token'

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })

      // Explicit verification: localStorage is completely empty and unused
      expect(localStorage.getItem('accessToken')).toBeNull()
      expect(localStorage.getItem('token')).toBeNull()
      expect(localStorage.getItem('jwt')).toBeNull()
      expect(localStorage.length).toBe(0)
    })

    it('does NOT store access token in sessionStorage', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive.token'

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })

      // Explicit verification: sessionStorage is completely empty and unused
      expect(sessionStorage.getItem('accessToken')).toBeNull()
      expect(sessionStorage.getItem('token')).toBeNull()
      expect(sessionStorage.getItem('jwt')).toBeNull()
      expect(sessionStorage.length).toBe(0)
    })

    it('does NOT place access token in URL query parameters or hash', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive.token'

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })

      expect(window.location.search).not.toContain(mockAccessToken)
      expect(window.location.hash).not.toContain(mockAccessToken)
    })

    it('does NOT log access token or password to console', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive.token'
      const secretPassword = 'MySecretP@ssword999'

      const consoleLogSpy = vi.spyOn(console, 'log')
      const consoleInfoSpy = vi.spyOn(console, 'info')
      const consoleWarnSpy = vi.spyOn(console, 'warn')
      const consoleErrorSpy = vi.spyOn(console, 'error')

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), secretPassword)
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })

      const allLoggedMessages = [
        ...consoleLogSpy.mock.calls.flat(),
        ...consoleInfoSpy.mock.calls.flat(),
        ...consoleWarnSpy.mock.calls.flat(),
        ...consoleErrorSpy.mock.calls.flat(),
      ].map(String)

      for (const msg of allLoggedMessages) {
        expect(msg).not.toContain(mockAccessToken)
        expect(msg).not.toContain(secretPassword)
      }
    })

    it('does NOT read or expose the HttpOnly refresh-token cookie via document.cookie', async () => {
      const user = userEvent.setup()
      const mockAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive.token'

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          accessToken: mockAccessToken,
          expiresAt: '2026-10-01T19:00:00Z',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('region', { name: /authenticated session/i })).toBeInTheDocument()
      })

      // document.cookie must not contain refreshToken
      expect(document.cookie).not.toContain('refreshToken')
    })
  })

  describe('7. Advanced Security & XSS Boundaries (Step 8)', () => {
    it('safely handles malicious XSS payloads in inputs without script execution or HTML injection', async () => {
      const user = userEvent.setup()
      renderLoginForm()

      const xssPayload = '<img src="x" />'
      const emailInput = screen.getByLabelText(/email address/i)
      await user.type(emailInput, xssPayload)

      expect(emailInput).toHaveValue(xssPayload)

      const submitBtn = screen.getByRole('button', { name: /sign in to vaultx/i })
      await user.click(submitBtn)

      expect(screen.getByText('Email format is invalid.')).toBeInTheDocument()
      expect(document.querySelector('img[src="x"]')).toBeNull()
    })

    it('safely renders unexpected server errors with generic fallback', async () => {
      const user = userEvent.setup()

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({
          detail: 'Database connection failed',
        }),
      } as Response)

      renderLoginForm()

      await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
      await user.type(screen.getByLabelText(/^master password/i), 'Password123!')
      await user.click(screen.getByRole('button', { name: /sign in to vaultx/i }))

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeInTheDocument()
      })

      expect(screen.getByRole('alert')).toHaveTextContent('Unable to sign in right now. Please try again.')
      expect(document.querySelector('script')).toBeNull()
    })
  })
})
