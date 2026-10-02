import React, { useState } from 'react'
import { AuthProvider } from './context/AuthContext'
import { LoginForm } from './components/LoginForm'
import { RegisterForm } from './components/RegisterForm'

export const App: React.FC = () => {
  const [activeView, setActiveView] = useState<'login' | 'register'>('login')

  return (
    <AuthProvider>
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
    </AuthProvider>
  )
}

export default App
