import { describe, expect, it } from 'vitest'
import {
  evaluatePasswordCriteria,
  validateConfirmPassword,
  validateEmail,
  validatePassword,
  validateRegisterForm,
  validateLoginPassword,
  validateLoginForm,
} from '../utils/validation'

describe('Validation Utilities', () => {
  describe('validateEmail', () => {
    it('rejects empty or whitespace email', () => {
      expect(validateEmail('')).toBe('Email is required.')
      expect(validateEmail('   ')).toBe('Email is required.')
    })

    it('rejects email exceeding 320 characters', () => {
      const longEmail = `${'a'.repeat(315)}@test.com`
      expect(validateEmail(longEmail)).toBe('Email must not exceed 320 characters.')
    })

    it('rejects invalid email formats', () => {
      expect(validateEmail('invalid-email')).toBe('Email format is invalid.')
      expect(validateEmail('user@')).toBe('Email format is invalid.')
      expect(validateEmail('@domain.com')).toBe('Email format is invalid.')
      expect(validateEmail('user..name@domain.com')).toBe('Email format is invalid.')
      expect(validateEmail('user@domain.com.')).toBe('Email format is invalid.')
    })

    it('accepts valid email formats', () => {
      expect(validateEmail('user@example.com')).toBeNull()
      expect(validateEmail('first.last@domain.co.uk')).toBeNull()
    })
  })

  describe('validatePassword', () => {
    it('rejects empty password', () => {
      expect(validatePassword('')).toBe('Password is required.')
    })

    it('rejects password shorter than 12 characters', () => {
      expect(validatePassword('Short1!Aa')).toBe('Password must be at least 12 characters long.')
    })

    it('rejects password longer than 128 characters', () => {
      const longPass = `${'A'.repeat(126)}1!a`
      expect(validatePassword(longPass)).toBe('Password must not exceed 128 characters.')
    })

    it('rejects password missing uppercase letter', () => {
      expect(validatePassword('alllowercase123!@#')).toBe('Password must contain at least one uppercase letter.')
    })

    it('rejects password missing lowercase letter', () => {
      expect(validatePassword('ALLUPPERCASE123!@#')).toBe('Password must contain at least one lowercase letter.')
    })

    it('rejects password missing digit', () => {
      expect(validatePassword('NoDigitsAtAll!@#Aa')).toBe('Password must contain at least one digit.')
    })

    it('rejects password missing special character', () => {
      expect(validatePassword('NoSpecialChars123Aa')).toBe('Password must contain at least one special character.')
    })

    it('accepts strong password meeting all criteria', () => {
      expect(validatePassword('StrongP@ssw0rd!123')).toBeNull()
    })
  })

  describe('evaluatePasswordCriteria', () => {
    it('evaluates all criteria flags correctly', () => {
      const result = evaluatePasswordCriteria('StrongP@ssw0rd!123')
      expect(result).toEqual({
        minLength: true,
        hasUpper: true,
        hasLower: true,
        hasDigit: true,
        hasSpecial: true,
      })
    })

    it('identifies missing criteria', () => {
      const result = evaluatePasswordCriteria('weak')
      expect(result.minLength).toBe(false)
      expect(result.hasUpper).toBe(false)
      expect(result.hasLower).toBe(true)
      expect(result.hasDigit).toBe(false)
      expect(result.hasSpecial).toBe(false)
    })
  })

  describe('validateConfirmPassword', () => {
    it('rejects empty confirmation', () => {
      expect(validateConfirmPassword('Password123!', '')).toBe('Password confirmation is required.')
    })

    it('rejects mismatched passwords', () => {
      expect(validateConfirmPassword('Password123!', 'Different123!')).toBe('Passwords do not match.')
    })

    it('accepts matching passwords', () => {
      expect(validateConfirmPassword('Password123!', 'Password123!')).toBeNull()
    })
  })

  describe('validateRegisterForm', () => {
    it('returns all errors for invalid form data', () => {
      const errors = validateRegisterForm({
        email: '',
        password: '',
        confirmPassword: '',
      })

      expect(errors.email).toBe('Email is required.')
      expect(errors.password).toBe('Password is required.')
      expect(errors.confirmPassword).toBe('Password confirmation is required.')
    })

    it('returns empty error map for valid form data', () => {
      const errors = validateRegisterForm({
        email: 'test@example.com',
        password: 'ValidPassword123!',
        confirmPassword: 'ValidPassword123!',
      })

      expect(Object.keys(errors)).toHaveLength(0)
    })
  })

  describe('validateLoginPassword', () => {
    it('rejects empty password', () => {
      expect(validateLoginPassword('')).toBe('Password is required.')
    })

    it('accepts any non-empty password without enforcing registration complexity policy', () => {
      expect(validateLoginPassword('simple')).toBeNull()
      expect(validateLoginPassword('ValidPassword123!')).toBeNull()
    })
  })

  describe('validateLoginForm', () => {
    it('returns all errors for empty form data', () => {
      const errors = validateLoginForm({
        email: '',
        password: '',
      })

      expect(errors.email).toBe('Email is required.')
      expect(errors.password).toBe('Password is required.')
    })

    it('rejects invalid email format', () => {
      const errors = validateLoginForm({
        email: 'invalid-email',
        password: 'anyPassword',
      })

      expect(errors.email).toBe('Email format is invalid.')
      expect(errors.password).toBeUndefined()
    })

    it('returns empty error map for valid form data', () => {
      const errors = validateLoginForm({
        email: 'user@example.com',
        password: 'anyPassword',
      })

      expect(Object.keys(errors)).toHaveLength(0)
    })
  })
})

