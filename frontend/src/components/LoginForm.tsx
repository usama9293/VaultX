import React, { useState } from 'react'
import { ApiError, loginUser } from '../api/auth'
import { useAuth } from '../context/useAuth'
import type { LoginFormErrors, LoginFormData, LoginResponse } from '../types/auth'
import { validateLoginForm } from '../utils/validation'
import './RegisterForm.css'
import './LoginForm.css'

interface LoginFormProps {
  onSubmit?: (data: LoginFormData) => Promise<LoginResponse | void> | LoginResponse | void
  onNavigateToRegister?: () => void
}

export const LoginForm: React.FC<LoginFormProps> = ({ onSubmit, onNavigateToRegister }) => {
  const { authState, setSession, logout } = useAuth()

  const [formData, setFormData] = useState<LoginFormData>({
    email: '',
    password: '',
  })

  const [errors, setErrors] = useState<LoginFormErrors>({})
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isLoggingOut, setIsLoggingOut] = useState(false)
  const [showPassword, setShowPassword] = useState(false)

  const handleLogout = async () => {
    if (isLoggingOut) return

    setIsLoggingOut(true)
    try {
      await logout()
    } catch {
      // Local auth state is already cleared by AuthContext.logout.
      // Do not claim server session revocation succeeded; do not expose raw errors.
    } finally {
      setIsLoggingOut(false)
    }
  }

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target
    setFormData((prev) => ({
      ...prev,
      [name]: value,
    }))

    // Clear field-specific error as user types
    if (errors[name as keyof LoginFormErrors]) {
      setErrors((prev) => ({
        ...prev,
        [name]: undefined,
      }))
    }
  }

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()

    if (isSubmitting) return

    // Run client-side validation
    const validationErrors = validateLoginForm(formData)
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    setErrors({})
    setIsSubmitting(true)

    try {
      let result: LoginResponse | void
      if (onSubmit) {
        result = await onSubmit(formData)
      } else {
        result = await loginUser(formData)
      }

      // Wipe sensitive password from component state
      setFormData({
        email: '',
        password: '',
      })

      if (result) {
        // Store access token in memory through AuthContext
        setSession(result)
      }
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          // Generic authentication error message to prevent user enumeration
          setErrors({ general: 'Invalid email or password.' })
        } else if (err.status === 400 && err.errorResponse?.errors) {
          const apiValidationErrors: LoginFormErrors = {}
          const errorMap = err.errorResponse.errors

          if (errorMap.Email && errorMap.Email.length > 0) {
            apiValidationErrors.email = errorMap.Email[0]
          }
          if (errorMap.Password && errorMap.Password.length > 0) {
            apiValidationErrors.password = errorMap.Password[0]
          }

          apiValidationErrors.general = err.message
          setErrors(apiValidationErrors)
        } else {
          setErrors({ general: err.message || 'Unable to sign in right now. Please try again.' })
        }
      } else {
        setErrors({ general: 'Unable to connect to the server. Please check your connection and try again.' })
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  if (authState.status === 'authenticated') {
    return (
      <div className="login-form-container" role="region" aria-label="Authenticated Session">
        <header className="form-header">
          <div className="brand-badge">VaultX Security</div>
          <h1 className="form-title">Authenticated</h1>
          <p className="form-subtitle">You are securely signed in to VaultX</p>
        </header>

        <div className="form-status-alert success" role="status" aria-live="polite">
          <p className="success-heading">Session Established</p>
          <p className="success-note">
            Your short-lived access token is held strictly in application memory. Vault creation, decryption, and protected resources will be available in future milestone steps.
          </p>
        </div>

        <button
          type="button"
          className="submit-btn logout-btn"
          onClick={handleLogout}
          disabled={isLoggingOut}
          aria-busy={isLoggingOut}
          aria-label={isLoggingOut ? 'Signing Out...' : 'Log out'}
        >
          {isLoggingOut ? (
            <>
              <span className="spinner" aria-hidden="true" />
              <span>Signing Out...</span>
            </>
          ) : (
            'Log Out'
          )}
        </button>

        <footer className="form-footer-note">
          Step 5 — Logout Frontend Connected
        </footer>
      </div>
    )
  }

  return (
    <div className="login-form-container">
      <header className="form-header">
        <div className="brand-badge">VaultX Security</div>
        <h1 className="form-title">Sign In</h1>
        <p className="form-subtitle">Enter your master credentials to access your vault</p>
      </header>

      {errors.general && (
        <div className="form-status-alert error" role="alert" aria-live="assertive">
          {errors.general}
        </div>
      )}

      <form className="login-form" onSubmit={handleSubmit} noValidate aria-label="Login Form">
        {/* Email Field */}
        <div className="form-group">
          <label htmlFor="login-email" className="form-label">
            Email Address
          </label>
          <div className="input-wrapper">
            <input
              id="login-email"
              name="email"
              type="email"
              autoComplete="username"
              className="form-input"
              value={formData.email}
              onChange={handleInputChange}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.email)}
              aria-describedby={errors.email ? 'login-email-error' : undefined}
              placeholder="name@example.com"
              required
            />
          </div>
          {errors.email && (
            <span id="login-email-error" className="error-message" role="alert">
              {errors.email}
            </span>
          )}
        </div>

        {/* Password Field */}
        <div className="form-group">
          <label htmlFor="login-password" className="form-label">
            Master Password
          </label>
          <div className="input-wrapper">
            <input
              id="login-password"
              name="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              className="form-input"
              value={formData.password}
              onChange={handleInputChange}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.password)}
              aria-describedby={errors.password ? 'login-password-error' : undefined}
              placeholder="Enter master password"
              required
            />
            <button
              type="button"
              className="toggle-password-btn"
              onClick={() => setShowPassword((prev) => !prev)}
              aria-label={showPassword ? 'Hide master password' : 'Show master password'}
              tabIndex={0}
            >
              {showPassword ? 'Hide' : 'Show'}
            </button>
          </div>
          {errors.password && (
            <span id="login-password-error" className="error-message" role="alert">
              {errors.password}
            </span>
          )}
        </div>

        {/* Submit Button */}
        <button
          type="submit"
          className="submit-btn"
          disabled={isSubmitting}
          aria-busy={isSubmitting}
        >
          {isSubmitting ? (
            <>
              <span className="spinner" aria-hidden="true" />
              <span>Signing In...</span>
            </>
          ) : (
            'Sign In to VaultX'
          )}
        </button>
      </form>

      {onNavigateToRegister && (
        <div className="form-navigation-note">
          Don't have an account?{' '}
          <button type="button" className="link-btn" onClick={onNavigateToRegister}>
            Create Account
          </button>
        </div>
      )}

      <footer className="form-footer-note">
        Step 5 — Connected to POST /api/auth/login
      </footer>
    </div>
  )
}
