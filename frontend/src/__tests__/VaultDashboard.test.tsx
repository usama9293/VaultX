import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../context/authContextDef'
import { VaultDashboard } from '../components/VaultDashboard'
import type { AuthState } from '../types/auth'

const authenticatedState: AuthState = {
  status: 'authenticated',
  accessToken: 'access-token-test',
  expiresAt: '2026-10-06T12:00:00Z',
}

function renderDashboard() {
  return render(
    <AuthContext.Provider value={{
      authState: authenticatedState,
      setSession: vi.fn(),
      clearSession: vi.fn(),
      logout: vi.fn().mockResolvedValue(undefined),
    }}>
      <VaultDashboard accessToken={authenticatedState.accessToken!} />
    </AuthContext.Provider>,
  )
}

const response = (status: number, body: unknown = {}) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body,
}) as Response

describe('Vault dashboard', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows the initialization state and creates the current user vault', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(response(404))
      .mockResolvedValueOnce(response(201, {
        id: 'vault-id',
        createdAt: '2026-10-06T10:00:00Z',
        updatedAt: '2026-10-06T10:00:00Z',
      }))
    const user = userEvent.setup()
    renderDashboard()

    expect(await screen.findByText('Set up your vault')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Initialize Vault' }))

    expect(await screen.findByText('No passwords yet')).toBeInTheDocument()
    const [getUrl, getRequest] = vi.mocked(fetch).mock.calls[0]
    expect(getUrl).toContain('/api/vault')
    expect(getRequest?.method).toBe('GET')
    expect(getRequest?.headers).toEqual({ Authorization: 'Bearer access-token-test' })
    const [postUrl, postRequest] = vi.mocked(fetch).mock.calls[1]
    expect(postUrl).toContain('/api/vault')
    expect(postRequest?.method).toBe('POST')
    expect(postRequest?.body).toBeUndefined()
    expect(postRequest?.headers).toEqual({ Authorization: 'Bearer access-token-test' })
    expect(screen.queryByRole('button', { name: /add login|search/i })).not.toBeInTheDocument()
  })

  it('shows an empty-vault state after loading an existing vault', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, {
      id: 'vault-id',
      createdAt: '2026-10-06T10:00:00Z',
      updatedAt: '2026-10-06T10:00:00Z',
    }))

    renderDashboard()

    expect(await screen.findByText('No passwords yet')).toBeInTheDocument()
  })

  it('shows errors and retries vault retrieval', async () => {
    vi.mocked(fetch)
      .mockRejectedValueOnce(new Error('network failure'))
      .mockResolvedValueOnce(response(404))
    const user = userEvent.setup()
    renderDashboard()

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load your vault.')
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('Set up your vault')).toBeInTheDocument()
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
  })
})
