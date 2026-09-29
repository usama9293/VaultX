import React, { useState } from 'react'
import type { FormErrors, RegisterFormData } from '../types/auth'
import { evaluatePasswordCriteria, validateRegisterForm } from '../utils/validation'
import './RegisterForm.css'

interface RegisterFormProps {
  onSubmit?: (data: RegisterFormData) => Promise<void> | void
}

export const RegisterForm: React.FC<RegisterFormProps> = ({ onSubmit }) => {
  const [formData, setFormData] = useState<RegisterFormData>({
    email: '',
    password: '',
    confirmPassword: '',
  })

  const [errors, setErrors] = useState<FormErrors>({})
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmPassword, setShowConfirmPassword] = useState(false)
  const [submitSuccessNotice, setSubmitSuccessNotice] = useState<string | null>(null)

  const passwordCriteria = evaluatePasswordCriteria(formData.password)

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target
    setFormData((prev) => ({
      ...prev,
      [name]: value,
    }))

    // Clear field-specific error as user types
    if (errors[name as keyof FormErrors]) {
      setErrors((prev) => ({
        ...prev,
        [name]: undefined,
      }))
    }
  }

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()

    if (isSubmitting) return

    setSubmitSuccessNotice(null)

    // Client-side validation check
    const validationErrors = validateRegisterForm(formData)
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    setErrors({})
    setIsSubmitting(true)

    try {
      if (onSubmit) {
        await onSubmit(formData)
      } else {
        // Step 6D local stub: Simulate local processing without API integration
        await new Promise((resolve) => setTimeout(resolve, 600))
        setSubmitSuccessNotice('Client validation passed. Form is ready for Step 6E API integration.')
      }
    } catch {
      setErrors({ general: 'An unexpected error occurred during submission.' })
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="register-form-container">
      <header className="form-header">
        <div className="brand-badge">VaultX Security</div>
        <h1 className="form-title">Create Account</h1>
        <p className="form-subtitle">Register to initialize your secure zero-knowledge vault</p>
      </header>

      {submitSuccessNotice && (
        <div className="form-status-alert info" role="status" aria-live="polite">
          {submitSuccessNotice}
        </div>
      )}

      {errors.general && (
        <div className="form-status-alert" role="alert" aria-live="assertive">
          {errors.general}
        </div>
      )}

      <form className="register-form" onSubmit={handleSubmit} noValidate aria-label="Registration Form">
        {/* Email Field */}
        <div className="form-group">
          <label htmlFor="email" className="form-label">
            Email Address
          </label>
          <div className="input-wrapper">
            <input
              id="email"
              name="email"
              type="email"
              autoComplete="username"
              className="form-input"
              value={formData.email}
              onChange={handleInputChange}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.email)}
              aria-describedby={errors.email ? 'email-error' : undefined}
              placeholder="name@example.com"
              required
            />
          </div>
          {errors.email && (
            <span id="email-error" className="error-message" role="alert">
              {errors.email}
            </span>
          )}
        </div>

        {/* Password Field */}
        <div className="form-group">
          <label htmlFor="password" className="form-label">
            Master Password
          </label>
          <div className="input-wrapper">
            <input
              id="password"
              name="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="new-password"
              className="form-input"
              value={formData.password}
              onChange={handleInputChange}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.password)}
              aria-describedby={errors.password ? 'password-error password-criteria' : 'password-criteria'}
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
            <span id="password-error" className="error-message" role="alert">
              {errors.password}
            </span>
          )}
        </div>

        {/* Password Criteria Checklist */}
        <div id="password-criteria" className="password-criteria" aria-label="Password requirements">
          <div className="criteria-title">Password Requirements</div>
          <ul className="criteria-list">
            <li className={`criteria-item ${passwordCriteria.minLength ? 'met' : ''}`}>
              <span className="criteria-bullet">{passwordCriteria.minLength ? '✓' : '•'}</span>
              12–128 characters
            </li>
            <li className={`criteria-item ${passwordCriteria.hasUpper ? 'met' : ''}`}>
              <span className="criteria-bullet">{passwordCriteria.hasUpper ? '✓' : '•'}</span>
              Uppercase letter
            </li>
            <li className={`criteria-item ${passwordCriteria.hasLower ? 'met' : ''}`}>
              <span className="criteria-bullet">{passwordCriteria.hasLower ? '✓' : '•'}</span>
              Lowercase letter
            </li>
            <li className={`criteria-item ${passwordCriteria.hasDigit ? 'met' : ''}`}>
              <span className="criteria-bullet">{passwordCriteria.hasDigit ? '✓' : '•'}</span>
              One number
            </li>
            <li className={`criteria-item ${passwordCriteria.hasSpecial ? 'met' : ''}`}>
              <span className="criteria-bullet">{passwordCriteria.hasSpecial ? '✓' : '•'}</span>
              Special character
            </li>
          </ul>
        </div>

        {/* Confirm Password Field */}
        <div className="form-group">
          <label htmlFor="confirmPassword" className="form-label">
            Confirm Master Password
          </label>
          <div className="input-wrapper">
            <input
              id="confirmPassword"
              name="confirmPassword"
              type={showConfirmPassword ? 'text' : 'password'}
              autoComplete="new-password"
              className="form-input"
              value={formData.confirmPassword}
              onChange={handleInputChange}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.confirmPassword)}
              aria-describedby={errors.confirmPassword ? 'confirm-error' : undefined}
              placeholder="Re-enter master password"
              required
            />
            <button
              type="button"
              className="toggle-password-btn"
              onClick={() => setShowConfirmPassword((prev) => !prev)}
              aria-label={showConfirmPassword ? 'Hide confirmed password' : 'Show confirmed password'}
              tabIndex={0}
            >
              {showConfirmPassword ? 'Hide' : 'Show'}
            </button>
          </div>
          {errors.confirmPassword && (
            <span id="confirm-error" className="error-message" role="alert">
              {errors.confirmPassword}
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
              <span>Registering Account...</span>
            </>
          ) : (
            'Create VaultX Account'
          )}
        </button>
      </form>

      <footer className="form-footer-note">
        Step 6D — Client-side Registration UI (Backend integration connects in Step 6E)
      </footer>
    </div>
  )
}
