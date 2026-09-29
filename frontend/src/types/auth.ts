export interface RegisterFormData {
  email: string
  password: string
  confirmPassword: string
}

export interface FormErrors {
  email?: string
  password?: string
  confirmPassword?: string
  general?: string
}

export interface PasswordCriteria {
  minLength: boolean
  hasUpper: boolean
  hasLower: boolean
  hasDigit: boolean
  hasSpecial: boolean
}
