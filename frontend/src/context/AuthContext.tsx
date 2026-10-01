import React, { useState, useMemo } from 'react'
import type { AuthState, LoginResponse } from '../types/auth'
import { AuthContext } from './authContextDef'

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  // In-memory authentication state: Access token is stored strictly in React memory
  // NEVER persisted to localStorage, sessionStorage, or JavaScript cookies.
  const [authState, setAuthState] = useState<AuthState>({
    status: 'unauthenticated',
    accessToken: null,
    expiresAt: null,
  })

  const setSession = (response: LoginResponse) => {
    setAuthState({
      status: 'authenticated',
      accessToken: response.accessToken,
      expiresAt: response.expiresAt,
    })
  }

  const clearSession = () => {
    setAuthState({
      status: 'unauthenticated',
      accessToken: null,
      expiresAt: null,
    })
  }

  const contextValue = useMemo(
    () => ({
      authState,
      setSession,
      clearSession,
    }),
    [authState]
  )

  return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>
}
