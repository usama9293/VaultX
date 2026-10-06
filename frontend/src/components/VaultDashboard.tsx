import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/auth'
import { getCurrentVault, initializeVault, type VaultResponse } from '../api/vault'
import { useAuth } from '../context/useAuth'
import './VaultDashboard.css'

type DashboardState = 'loading' | 'uninitialized' | 'empty' | 'error'

export function VaultDashboard({ accessToken }: { accessToken: string }) {
  const { logout } = useAuth()
  const [state, setState] = useState<DashboardState>('loading')
  const [vault, setVault] = useState<VaultResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isInitializing, setIsInitializing] = useState(false)
  const [isLoggingOut, setIsLoggingOut] = useState(false)

  const loadVault = useCallback(() => getCurrentVault(accessToken), [accessToken])

  useEffect(() => {
    let active = true
    void loadVault()
      .then(currentVault => {
        if (active) {
          setVault(currentVault)
          setState(currentVault ? 'empty' : 'uninitialized')
        }
      })
      .catch(requestError => {
        if (active) {
          setState('error')
          setError(requestError instanceof ApiError
            ? requestError.message
            : 'Unable to load your vault. Please try again.')
        }
      })
    return () => {
      active = false
    }
  }, [loadVault])

  const handleInitialize = async () => {
    setIsInitializing(true)
    setError(null)
    try {
      setVault(await initializeVault(accessToken))
      setState('empty')
    } catch (requestError) {
      setState('error')
      setError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to initialize your vault. Please try again.')
    } finally {
      setIsInitializing(false)
    }
  }

  const handleLogout = async () => {
    setIsLoggingOut(true)
    try {
      await logout()
    } catch {
      // AuthProvider clears local state even when server logout fails.
    } finally {
      setIsLoggingOut(false)
    }
  }

  return (
    <section className="vault-dashboard" role="region" aria-label="Authenticated Session">
      <header className="vault-dashboard-header">
        <div>
          <div className="brand-badge">VaultX Security</div>
          <h1 className="form-title">Your Vault</h1>
        </div>
        <button
          type="button"
          className="link-btn"
          onClick={handleLogout}
          disabled={isLoggingOut}
          aria-busy={isLoggingOut}
        >
          {isLoggingOut ? 'Signing Out...' : 'Log Out'}
        </button>
      </header>

      {state === 'loading' && (
        <div className="vault-dashboard-state" role="status" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          <span>Loading your vault...</span>
        </div>
      )}

      {state === 'uninitialized' && (
        <div className="vault-dashboard-state">
          <h2>Set up your vault</h2>
          <p>Your personal vault is ready to be initialized.</p>
          <button
            type="button"
            className="submit-btn"
            onClick={handleInitialize}
            disabled={isInitializing}
            aria-busy={isInitializing}
          >
            {isInitializing ? 'Initializing...' : 'Initialize Vault'}
          </button>
        </div>
      )}

      {state === 'empty' && vault && (
        <div className="vault-dashboard-state">
          <div className="form-status-alert success" role="status" aria-live="polite">
            <p className="success-heading">Vault ready</p>
          </div>
          <h2>No passwords yet</h2>
          <p>Your vault is empty. Password entries will be available in a later phase.</p>
        </div>
      )}

      {state === 'error' && (
        <div className="vault-dashboard-state">
          <div className="form-status-alert error" role="alert" aria-live="assertive">
            {error}
          </div>
          <button
            type="button"
            className="submit-btn"
            onClick={() => {
              setState('loading')
              setError(null)
              void loadVault()
                .then(currentVault => {
                  setVault(currentVault)
                  setState(currentVault ? 'empty' : 'uninitialized')
                })
                .catch(requestError => {
                  setState('error')
                  setError(requestError instanceof ApiError
                    ? requestError.message
                    : 'Unable to load your vault. Please try again.')
                })
            }}
          >
            Retry
          </button>
        </div>
      )}
    </section>
  )
}
