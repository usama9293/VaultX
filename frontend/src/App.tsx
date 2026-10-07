import React, { useCallback, useEffect, useState } from 'react'
import { ApiError, refreshSession } from './api/auth'
import { AuthProvider } from './context/AuthContext'
import { useAuth } from './context/useAuth'
import { LoginForm } from './components/LoginForm'
import { RegisterForm } from './components/RegisterForm'
import { VaultDashboard } from './components/VaultDashboard'

const AppContent: React.FC = () => {
  const [activeView, setActiveView] = useState<'login' | 'register'>('login')
  const [isRestoringSession, setIsRestoringSession] = useState(true)
  const [sessionError, setSessionError] = useState<string | null>(null)
  const { authState, setSession, clearSession } = useAuth()

  const restoreSession = useCallback(async () => {
    try {
      const session = await refreshSession()
      if (session) {
        setSession(session)
      } else {
        clearSession()
      }
    } catch (error) {
      setSessionError(error instanceof ApiError
        ? error.message
        : 'Unable to restore your session. Please try again.')
    } finally {
      setIsRestoringSession(false)
    }
  }, [clearSession, setSession])

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- Session restoration updates state after the refresh request completes.
    void restoreSession()
  }, [restoreSession])

  if (isRestoringSession) {
    return (
      <main>
        <div className="form-status-alert" role="status" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          <span>Checking your session...</span>
        </div>
      </main>
    )
  }

  if (sessionError) {
    return (
      <main>
        <div className="login-form-container">
          <div className="form-status-alert error" role="alert" aria-live="assertive">
            {sessionError}
          </div>
          <button
            type="button"
            className="submit-btn"
            onClick={() => {
              setIsRestoringSession(true)
              setSessionError(null)
              void restoreSession()
            }}
          >
            Retry
          </button>
        </div>
      </main>
    )
  }

  if (authState.status === 'authenticated' && authState.accessToken) {
    return (
      <main>
        <VaultDashboard key={authState.accessToken} accessToken={authState.accessToken} />
      </main>
    )
  }

  return (
    <main>
      <nav
        className="auth-nav-tabs"
        aria-label="Authentication Navigation"
        style={{
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          gap: '1rem',
          marginBottom: '1.5rem',
        }}
      >
        <button
          type="button"
          className="link-btn"
          style={{
            fontWeight: activeView === 'login' ? 700 : 400,
            textDecoration: activeView === 'login' ? 'underline' : 'none',
          }}
          onClick={() => setActiveView('login')}
        >
          Sign In
        </button>
        <span style={{ color: 'var(--text-secondary)' }}>|</span>
        <button
          type="button"
          className="link-btn"
          style={{
            fontWeight: activeView === 'register' ? 700 : 400,
            textDecoration: activeView === 'register' ? 'underline' : 'none',
          }}
          onClick={() => setActiveView('register')}
        >
          Create Account
        </button>
      </nav>

      {activeView === 'login' ? (
        <LoginForm onNavigateToRegister={() => setActiveView('register')} />
      ) : (
        <RegisterForm />
      )}
    </main>
  )
}

export const App: React.FC = () => (
  <AuthProvider>
    <AppContent />
  </AuthProvider>
)

export default App
