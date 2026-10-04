import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RegisterForm } from '../components/RegisterForm'

describe('RegisterForm Component (Integration)', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    localStorage.clear()
    sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('1. renders all registration form fields and accessible labels', () => {
    render(<RegisterForm />)

    expect(screen.queryByText(/step 6e|connected to post|integration complete|future milestone/i)).not.toBeInTheDocument()

    expect(screen.getByRole('heading', { name: /create account/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^master password/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/confirm master password/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /create vaultx account/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/password requirements/i)).toBeInTheDocument()
  })

  it('2. empty submission displays required validation errors without calling API', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Email is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByText('Password confirmation is required.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('3. rejects password mismatch on client without calling API', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'ValidPassword123!')
    await user.type(screen.getByLabelText(/confirm master password/i), 'MismatchPassword123!')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('4. successful API response displays success state and clears sensitive inputs', async () => {
    const user = userEvent.setup()

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({
        id: 'user-guid-1234',
        email: 'newuser@example.com',
        createdAt: '2026-09-29T12:00:00Z',
        updatedAt: '2026-09-29T12:00:00Z',
      }),
    } as Response)

    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'newuser@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'StrongP@ssw0rd!123')
    await user.type(screen.getByLabelText(/confirm master password/i), 'StrongP@ssw0rd!123')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /registration successful/i })).toBeInTheDocument()
      expect(screen.getByText(/newuser@example.com/i)).toBeInTheDocument()
    })
    expect(screen.queryByText(/step 6e|connected to post|integration complete|future milestone/i)).not.toBeInTheDocument()

    // 4. No password is displayed after successful registration
    expect(screen.queryByText('StrongP@ssw0rd!123')).toBeNull()
    expect(screen.queryByDisplayValue('StrongP@ssw0rd!123')).toBeNull()

    // 5. No authentication token is created in storage or URL
    expect(localStorage.getItem('password')).toBeNull()
    expect(sessionStorage.getItem('password')).toBeNull()
    expect(localStorage.getItem('token')).toBeNull()
    expect(localStorage.getItem('jwt')).toBeNull()
    expect(sessionStorage.getItem('token')).toBeNull()
    expect(sessionStorage.getItem('jwt')).toBeNull()
    expect(window.location.search).not.toContain('password')

    // 6. No vault is created
    expect(localStorage.getItem('vault')).toBeNull()
    expect(sessionStorage.getItem('vault')).toBeNull()
  })

  it('5. duplicate email response (409) displays conflict error', async () => {
    const user = userEvent.setup()

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

    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'existing@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'StrongP@ssw0rd!123')
    await user.type(screen.getByLabelText(/confirm master password/i), 'StrongP@ssw0rd!123')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    await waitFor(() => {
      expect(screen.getAllByText('A user with this email already exists.').length).toBeGreaterThanOrEqual(1)
    })
  })

  it('6. backend validation error (400) displays field errors', async () => {
    const user = userEvent.setup()

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

    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'StrongP@ssw0rd!123')
    await user.type(screen.getByLabelText(/confirm master password/i), 'StrongP@ssw0rd!123')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    await waitFor(() => {
      expect(screen.getByText('Email format is invalid.')).toBeInTheDocument()
    })
  })

  it('7. network failure displays safe connection error', async () => {
    const user = userEvent.setup()

    vi.mocked(fetch).mockRejectedValueOnce(new Error('Network offline'))

    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'test@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'StrongP@ssw0rd!123')
    await user.type(screen.getByLabelText(/confirm master password/i), 'StrongP@ssw0rd!123')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    await waitFor(() => {
      expect(screen.getByText(/unable to connect to the server/i)).toBeInTheDocument()
    })
  })

  it('8. prevents duplicate submissions while request is in flight', async () => {
    const user = userEvent.setup()

    let resolveFetch: (value: Response) => void = () => {}
    const pendingPromise = new Promise<Response>((resolve) => {
      resolveFetch = resolve
    })

    vi.mocked(fetch).mockReturnValueOnce(pendingPromise)

    render(<RegisterForm />)

    const emailInput = screen.getByLabelText(/email address/i)
    const passwordInput = screen.getByLabelText(/^master password/i)
    const confirmInput = screen.getByLabelText(/confirm master password/i)
    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })

    await user.type(emailInput, 'user@example.com')
    await user.type(passwordInput, 'StrongP@ssw0rd!123')
    await user.type(confirmInput, 'StrongP@ssw0rd!123')

    // Click submit
    await user.click(submitBtn)

    // Form inputs and button should be disabled
    expect(emailInput).toBeDisabled()
    expect(passwordInput).toBeDisabled()
    expect(confirmInput).toBeDisabled()
    expect(submitBtn).toBeDisabled()
    expect(screen.getByText('Registering Account...')).toBeInTheDocument()

    // Second click should be ignored
    await user.click(submitBtn)
    expect(fetch).toHaveBeenCalledTimes(1)

    // Complete response
    resolveFetch({
      ok: true,
      status: 201,
      json: async () => ({
        id: '123',
        email: 'user@example.com',
        createdAt: '2026-09-29T12:00:00Z',
        updatedAt: '2026-09-29T12:00:00Z',
      }),
    } as Response)

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /registration successful/i })).toBeInTheDocument()
    })
  })

  it('9. password visibility toggle switches between text and password types', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    const toggleBtn = screen.getByRole('button', { name: /show master password/i })

    expect(passwordInput).toHaveAttribute('type', 'password')

    await user.click(toggleBtn)

    expect(passwordInput).toHaveAttribute('type', 'text')
    expect(screen.getByRole('button', { name: /hide master password/i })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /hide master password/i }))

    expect(passwordInput).toHaveAttribute('type', 'password')
  })

  it('10. rejects invalid email on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'not-an-email')
    await user.type(screen.getByLabelText(/^master password/i), 'StrongP@ssw0rd!123')
    await user.type(screen.getByLabelText(/confirm master password/i), 'StrongP@ssw0rd!123')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Email format is invalid.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('11. rejects short password on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'weak')
    await user.type(screen.getByLabelText(/confirm master password/i), 'weak')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Password must be at least 12 characters long.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('12. rejects missing uppercase on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'alllowercase123!@#')
    await user.type(screen.getByLabelText(/confirm master password/i), 'alllowercase123!@#')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Password must contain at least one uppercase letter.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('13. rejects missing lowercase on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'ALLUPPERCASE123!@#')
    await user.type(screen.getByLabelText(/confirm master password/i), 'ALLUPPERCASE123!@#')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Password must contain at least one lowercase letter.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('14. rejects missing digit on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'NoDigitsAtAll!@#Aa')
    await user.type(screen.getByLabelText(/confirm master password/i), 'NoDigitsAtAll!@#Aa')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Password must contain at least one digit.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('15. rejects missing special character on client and prevents API call', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    await user.type(screen.getByLabelText(/email address/i), 'valid@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'NoSpecialChars123Aa')
    await user.type(screen.getByLabelText(/confirm master password/i), 'NoSpecialChars123Aa')

    await user.click(screen.getByRole('button', { name: /create vaultx account/i }))

    expect(screen.getByText('Password must contain at least one special character.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })
})
