import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createVaultEntry,
  deleteVaultEntry,
  getVaultEntries,
  getVaultEntry,
  updateVaultEntry,
} from '../api/vaultEntries'

const response = (status: number, body: unknown = {}) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body,
}) as Response

const metadata = {
  id: 'entry-id',
  title: 'Example',
  websiteUrl: null,
  username: 'example-user',
  createdAt: '2026-10-06T10:00:00Z',
  updatedAt: '2026-10-06T10:00:00Z',
}

describe('vault entry API client', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('uses authenticated search and strips unexpected fields from list responses', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, {
      items: [{ ...metadata, notes: 'not listed', vaultId: 'private', password: 'secret' }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    }))

    const result = await getVaultEntries('token', 'example', 1, 20)

    expect(result.items[0]).toEqual(metadata)
    expect(Object.keys(result.items[0])).toEqual([
      'id', 'title', 'websiteUrl', 'username', 'createdAt', 'updatedAt',
    ])
    const [url, init] = vi.mocked(fetch).mock.calls[0]
    const parsedUrl = new URL(String(url), 'http://localhost')
    expect(parsedUrl.pathname).toBe('/api/vault/entries')
    expect(parsedUrl.searchParams.get('q')).toBe('example')
    expect(init?.headers).toEqual({ Authorization: 'Bearer token' })
  })

  it('maps single-entry results through a strict metadata allowlist', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, {
      ...metadata,
      notes: 'details',
      userId: 'private',
      encryptedPassword: 'secret',
      passwordNonce: 'secret',
      passwordAuthenticationTag: 'secret',
    }))

    const result = await getVaultEntry('token', 'entry-id')

    expect(result).toEqual({ ...metadata, notes: 'details' })
    expect(Object.keys(result).sort()).toEqual([
      'createdAt', 'id', 'notes', 'title', 'updatedAt', 'username', 'websiteUrl',
    ])
  })

  it('sends only metadata fields for create and update', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(response(201, { ...metadata, notes: null }))
      .mockResolvedValueOnce(response(200, { ...metadata, notes: 'updated' }))
    const input = {
      title: 'Example',
      websiteUrl: null,
      username: 'example-user',
      notes: null,
    }

    await createVaultEntry('token', input)
    await updateVaultEntry('token', 'entry-id', { ...input, notes: 'updated' })

    for (const [, init] of vi.mocked(fetch).mock.calls) {
      expect(init?.headers).toEqual({
        Authorization: 'Bearer token',
        'Content-Type': 'application/json',
      })
      expect(Object.keys(JSON.parse(String(init?.body))).sort()).toEqual([
        'notes', 'title', 'username', 'websiteUrl',
      ])
    }
    expect(vi.mocked(fetch).mock.calls[0][1]?.method).toBe('POST')
    expect(vi.mocked(fetch).mock.calls[1][1]?.method).toBe('PUT')
  })

  it('sends authenticated delete without a request body', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(204))

    await deleteVaultEntry('token', 'entry-id')

    expect(vi.mocked(fetch).mock.calls[0][1]).toEqual({
      method: 'DELETE',
      headers: { Authorization: 'Bearer token' },
    })
  })
})
