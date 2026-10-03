import { createContext } from 'react'
import type { AuthState, LoginResponse } from '../types/auth'

export interface AuthContextType {
  authState: AuthState
  setSession: (response: LoginResponse) => void
  clearSession: () => void
  /**
   * Attempts server logout, then always clears in-memory auth state.
   * Local state is cleared even if the API request fails.
   * API errors are rethrown after local cleanup so callers can handle them
   * without falsely claiming server session revocation succeeded.
   */
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined)
