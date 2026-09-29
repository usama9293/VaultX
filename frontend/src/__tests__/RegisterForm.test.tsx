import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { RegisterForm } from '../components/RegisterForm'

describe('RegisterForm Component', () => {
  it('1. renders all registration form fields and accessible labels', () => {
    render(<RegisterForm />)

    expect(screen.getByRole('heading', { name: /create account/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^master password/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/confirm master password/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /create vaultx account/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/password requirements/i)).toBeInTheDocument()
  })

  it('2. empty submission displays required validation errors', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Email is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByText('Password confirmation is required.')).toBeInTheDocument()
  })

  it('3. rejects invalid email formats', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const emailInput = screen.getByLabelText(/email address/i)
    await user.type(emailInput, 'notanemail')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Email format is invalid.')).toBeInTheDocument()
  })

  it('4. rejects password shorter than 12 characters', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    await user.type(passwordInput, 'Short1!a')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Password must be at least 12 characters long.')).toBeInTheDocument()
  })

  it('5. rejects password missing uppercase letter', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    await user.type(passwordInput, 'lowercaseonly123!')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Password must contain at least one uppercase letter.')).toBeInTheDocument()
  })

  it('6. rejects password missing lowercase letter', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    await user.type(passwordInput, 'UPPERCASEONLY123!')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Password must contain at least one lowercase letter.')).toBeInTheDocument()
  })

  it('7. rejects password missing digit', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    await user.type(passwordInput, 'NoDigitsInPassword!')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Password must contain at least one digit.')).toBeInTheDocument()
  })

  it('8. rejects password missing special character', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const passwordInput = screen.getByLabelText(/^master password/i)
    await user.type(passwordInput, 'NoSpecialChars123')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Password must contain at least one special character.')).toBeInTheDocument()
  })

  it('9. rejects mismatched password confirmation', async () => {
    const user = userEvent.setup()
    render(<RegisterForm />)

    const emailInput = screen.getByLabelText(/email address/i)
    const passwordInput = screen.getByLabelText(/^master password/i)
    const confirmInput = screen.getByLabelText(/confirm master password/i)

    await user.type(emailInput, 'valid@example.com')
    await user.type(passwordInput, 'ValidPassword123!')
    await user.type(confirmInput, 'DifferentPassword123!')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument()
  })

  it('10. valid form passes client-side validation and invokes submit handler', async () => {
    const user = userEvent.setup()
    const onSubmitMock = vi.fn().mockResolvedValue(undefined)

    render(<RegisterForm onSubmit={onSubmitMock} />)

    await user.type(screen.getByLabelText(/email address/i), 'user@example.com')
    await user.type(screen.getByLabelText(/^master password/i), 'ValidPassword123!')
    await user.type(screen.getByLabelText(/confirm master password/i), 'ValidPassword123!')

    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })
    await user.click(submitBtn)

    await waitFor(() => {
      expect(onSubmitMock).toHaveBeenCalledTimes(1)
      expect(onSubmitMock).toHaveBeenCalledWith({
        email: 'user@example.com',
        password: 'ValidPassword123!',
        confirmPassword: 'ValidPassword123!',
      })
    })
  })

  it('11. submit/loading state disables controls and indicates progress', async () => {
    const user = userEvent.setup()
    let resolveSubmit: () => void = () => {}
    const pendingPromise = new Promise<void>((resolve) => {
      resolveSubmit = resolve
    })
    const onSubmitMock = vi.fn().mockReturnValue(pendingPromise)

    render(<RegisterForm onSubmit={onSubmitMock} />)

    const emailInput = screen.getByLabelText(/email address/i)
    const passwordInput = screen.getByLabelText(/^master password/i)
    const confirmInput = screen.getByLabelText(/confirm master password/i)
    const submitBtn = screen.getByRole('button', { name: /create vaultx account/i })

    await user.type(emailInput, 'user@example.com')
    await user.type(passwordInput, 'ValidPassword123!')
    await user.type(confirmInput, 'ValidPassword123!')

    await user.click(submitBtn)

    // Form inputs and submit button should be disabled during submission
    expect(emailInput).toBeDisabled()
    expect(passwordInput).toBeDisabled()
    expect(confirmInput).toBeDisabled()
    expect(submitBtn).toBeDisabled()
    expect(screen.getByText('Registering Account...')).toBeInTheDocument()

    // Resolve submission
    resolveSubmit()

    await waitFor(() => {
      expect(submitBtn).not.toBeDisabled()
      expect(screen.getByText('Create VaultX Account')).toBeInTheDocument()
    })
  })

  it('12. password visibility toggle switches between text and password types', async () => {
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
})
