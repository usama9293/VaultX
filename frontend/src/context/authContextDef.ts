import { createContext } from 'react'
import type { AuthState, LoginResponse } from '../types/auth'

export interface AuthContextType {
  authState: AuthState
  setSession: (response: LoginResponse) => void
  clearSession: () => void
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined)
