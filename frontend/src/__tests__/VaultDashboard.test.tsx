import { render, screen, waitFor, within } from '@testing-library/react'
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

const emptyPage = {
  items: [],
  page: 1,
  pageSize: 20,
  hasMore: false,
}

const entryPage = (title = 'Example account') => ({
  items: [{
    id: 'entry-id',
    title,
    websiteUrl: 'https://example.test',
    username: 'user@example.test',
    createdAt: '2026-10-06T10:00:00Z',
    updatedAt: '2026-10-06T10:00:00Z',
  }],
  page: 1,
  pageSize: 20,
  hasMore: false,
})

const entryDetails = (title = 'Example account') => ({
  id: 'entry-id',
  title,
  websiteUrl: 'https://example.test',
  username: 'user@example.test',
  notes: '<script>not executable</script>',
  createdAt: '2026-10-06T10:00:00Z',
  updatedAt: '2026-10-06T10:00:00Z',
})

describe('Vault dashboard entries', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('initializes only the authenticated vault and displays metadata-only guidance', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(response(404))
      .mockResolvedValueOnce(response(201, {
        id: 'vault-id',
        createdAt: '2026-10-06T10:00:00Z',
        updatedAt: '2026-10-06T10:00:00Z',
      }))
      .mockResolvedValueOnce(response(200, emptyPage))
    const user = userEvent.setup()
    renderDashboard()

    expect(await screen.findByText('Set up your vault')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Initialize Vault' }))

    expect(await screen.findByText('No entries yet')).toBeInTheDocument()
    expect(screen.getByText(/metadata only\. Passwords are not stored yet/i)).toBeInTheDocument()
    const [vaultGetUrl, vaultGetRequest] = vi.mocked(fetch).mock.calls[0]
    expect(vaultGetUrl).toContain('/api/vault')
    expect(vaultGetRequest?.method).toBe('GET')
    const [vaultPostUrl, vaultPostRequest] = vi.mocked(fetch).mock.calls[1]
    expect(vaultPostUrl).toContain('/api/vault')
    expect(vaultPostRequest?.method).toBe('POST')
    expect(vaultPostRequest?.body).toBeUndefined()
    expect(vi.mocked(fetch).mock.calls[2][0]).toContain('/api/vault/entries')
  })

  it('lists, opens, edits and deletes metadata without persistent browser storage', async () => {
    const localStorageWrite = vi.spyOn(Storage.prototype, 'setItem')
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(fetch)
      .mockResolvedValueOnce(response(200, {
        id: 'vault-id',
        createdAt: '2026-10-06T10:00:00Z',
        updatedAt: '2026-10-06T10:00:00Z',
      }))
      .mockResolvedValueOnce(response(200, entryPage()))
      .mockResolvedValueOnce(response(200, entryDetails()))
      .mockResolvedValueOnce(response(200, entryDetails('Updated account')))
      .mockResolvedValueOnce(response(200, entryPage('Updated account')))
      .mockResolvedValueOnce(response(204))
      .mockResolvedValueOnce(response(200, emptyPage))
    const user = userEvent.setup()
    renderDashboard()

    await user.click(await screen.findByRole('button', { name: /Example account/ }))
    expect(await screen.findByText('<script>not executable</script>')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'https://example.test' })).toHaveAttribute(
      'rel', 'noopener noreferrer',
    )
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    const title = screen.getByLabelText('Title')
    await user.clear(title)
    await user.type(title, 'Updated account')
    await user.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByRole('heading', { name: 'Updated account' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    expect(confirm).toHaveBeenCalledOnce()
    expect(await screen.findByText('No entries yet')).toBeInTheDocument()
    expect(localStorageWrite).not.toHaveBeenCalled()
    expect(vi.mocked(fetch).mock.calls[3][0]).toContain('/api/vault/entries/entry-id')
    expect(vi.mocked(fetch).mock.calls[3][1]?.method).toBe('PUT')
    expect(vi.mocked(fetch).mock.calls[5][1]?.method).toBe('DELETE')
  })

  it('creates entries using only metadata and sends search as a query parameter', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(response(200, {
        id: 'vault-id',
        createdAt: '2026-10-06T10:00:00Z',
        updatedAt: '2026-10-06T10:00:00Z',
      }))
      .mockResolvedValueOnce(response(200, emptyPage))
      .mockResolvedValueOnce(response(201, entryDetails('Example account')))
      .mockResolvedValueOnce(response(200, entryPage()))
      .mockResolvedValueOnce(response(200, entryPage()))
    const user = userEvent.setup()
    renderDashboard()

    await user.click(await screen.findByRole('button', { name: 'Add Entry' }))
    await user.type(screen.getByLabelText('Title'), 'Example account')
    await user.type(screen.getByLabelText('Website URL (optional)'), 'https://example.test')
    await user.type(screen.getByLabelText('Username'), 'user@example.test')
    await user.type(screen.getByLabelText('Notes (optional)'), 'Only metadata')
    await user.click(screen.getByRole('button', { name: 'Create Entry' }))

    expect(await screen.findByRole('heading', { name: 'Example account' })).toBeInTheDocument()
    const createRequest = vi.mocked(fetch).mock.calls[2][1]
    const createBody = JSON.parse(String(createRequest?.body)) as Record<string, unknown>
    expect(createBody).toEqual({
      title: 'Example account',
      websiteUrl: 'https://example.test',
      username: 'user@example.test',
      notes: 'Only metadata',
    })
    expect(Object.keys(createBody)).not.toContain('password')
    expect(Object.keys(createBody)).not.toContain('vaultId')
    await user.click(screen.getByRole('button', { name: 'Back to entries' }))
    await user.type(screen.getByRole('textbox', { name: /Search title/ }), 'example')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(5))
    const searchUrl = new URL(String(vi.mocked(fetch).mock.calls[4][0]), 'http://localhost')
    expect(searchUrl.searchParams.get('q')).toBe('example')
    expect(within(screen.getByRole('list', { name: 'Vault entries' }))
      .getByText('Example account')).toBeInTheDocument()
  })

  it('shows request failures and retries vault retrieval', async () => {
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
