import React, { useState, useMemo, useCallback } from 'react'
import type { AuthState, LoginResponse } from '../types/auth'
import { logoutUser } from '../api/auth'
import { AuthContext } from './authContextDef'

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  // In-memory authentication state: Access token is stored strictly in React memory
  // NEVER persisted to localStorage, sessionStorage, or JavaScript cookies.
  const [authState, setAuthState] = useState<AuthState>({
    status: 'unauthenticated',
    accessToken: null,
    expiresAt: null,
  })

  const setSession = useCallback((response: LoginResponse) => {
    setAuthState({
      status: 'authenticated',
      accessToken: response.accessToken,
      expiresAt: response.expiresAt,
    })
  }, [])

  const clearSession = useCallback(() => {
    setAuthState({
      status: 'unauthenticated',
      accessToken: null,
      expiresAt: null,
    })
  }, [])

  /**
   * Local logout always clears in-memory auth state.
   * Server logout is best-effort: if the API call fails, local state is still cleared
   * and the original error is re-thrown so callers do not falsely claim server success.
   */
  const logout = useCallback(async () => {
    let apiError: unknown

    try {
      await logoutUser()
    } catch (error) {
      apiError = error
    } finally {
      // Always clear local authentication state — even on network/API failure
      setAuthState({
        status: 'unauthenticated',
        accessToken: null,
        expiresAt: null,
      })
    }

    if (apiError !== undefined) {
      throw apiError
    }
  }, [])

  const contextValue = useMemo(
    () => ({
      authState,
      setSession,
      clearSession,
      logout,
    }),
    [authState, setSession, clearSession, logout]
  )

  return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>
}
