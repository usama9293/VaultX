import type { FormErrors, PasswordCriteria, RegisterFormData } from '../types/auth'

const EMAIL_REGEX = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/

export function validateEmail(email: string): string | null {
  if (!email || !email.trim()) {
    return 'Email is required.'
  }

  const trimmed = email.trim()

  if (trimmed.length > 320) {
    return 'Email must not exceed 320 characters.'
  }

  if (trimmed.startsWith('.') || trimmed.endsWith('.') || trimmed.includes('..') || !EMAIL_REGEX.test(trimmed)) {
    return 'Email format is invalid.'
  }

  return null
}

export function evaluatePasswordCriteria(password: string): PasswordCriteria {
  return {
    minLength: password.length >= 12 && password.length <= 128,
    hasUpper: /[A-Z]/.test(password),
    hasLower: /[a-z]/.test(password),
    hasDigit: /[0-9]/.test(password),
    hasSpecial: /[^a-zA-Z0-9]/.test(password),
  }
}

export function validatePassword(password: string): string | null {
  if (!password) {
    return 'Password is required.'
  }

  if (password.length < 12) {
    return 'Password must be at least 12 characters long.'
  }

  if (password.length > 128) {
    return 'Password must not exceed 128 characters.'
  }

  if (!/[A-Z]/.test(password)) {
    return 'Password must contain at least one uppercase letter.'
  }

  if (!/[a-z]/.test(password)) {
    return 'Password must contain at least one lowercase letter.'
  }

  if (!/[0-9]/.test(password)) {
    return 'Password must contain at least one digit.'
  }

  if (!/[^a-zA-Z0-9]/.test(password)) {
    return 'Password must contain at least one special character.'
  }

  return null
}

export function validateConfirmPassword(password: string, confirmPassword: string): string | null {
  if (!confirmPassword) {
    return 'Password confirmation is required.'
  }

  if (password !== confirmPassword) {
    return 'Passwords do not match.'
  }

  return null
}

export function validateRegisterForm(data: RegisterFormData): FormErrors {
  const errors: FormErrors = {}

  const emailError = validateEmail(data.email)
  if (emailError) {
    errors.email = emailError
  }

  const passwordError = validatePassword(data.password)
  if (passwordError) {
    errors.password = passwordError
  }

  const confirmError = validateConfirmPassword(data.password, data.confirmPassword)
  if (confirmError) {
    errors.confirmPassword = confirmError
  }

  return errors
}
