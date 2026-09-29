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

export interface RegisterUserRequest {
  email: string
  password: string
  confirmPassword: string
}

export interface UserResponse {
  id: string
  email: string
  createdAt: string
  updatedAt: string
}

export interface ApiErrorResponse {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
