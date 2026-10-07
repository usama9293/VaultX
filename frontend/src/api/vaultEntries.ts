import { ApiError } from './auth'

export interface VaultEntry {
  id: string
  title: string
  websiteUrl: string | null
  username: string
  notes: string | null
  createdAt: string
  updatedAt: string
}

export interface VaultEntryListItem {
  id: string
  title: string
  websiteUrl: string | null
  username: string
  createdAt: string
  updatedAt: string
}

export interface VaultEntryPage {
  items: VaultEntryListItem[]
  page: number
  pageSize: number
  hasMore: boolean
}

export interface VaultEntryInput {
  title: string
  websiteUrl: string | null
  username: string
  notes: string | null
}

const endpoint = () => `${import.meta.env.VITE_API_BASE_URL || ''}/api/vault/entries`

export async function getVaultEntries(
  accessToken: string,
  query = '',
  page = 1,
  pageSize = 20,
): Promise<VaultEntryPage> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (query.trim()) {
    params.set('q', query.trim())
  }
  const response = await request(`${endpoint()}?${params}`, accessToken, 'GET')
  const body: unknown = await readJson(response)
  if (
    isObject(body)
    && Array.isArray(body.items)
    && typeof body.page === 'number'
    && typeof body.pageSize === 'number'
    && typeof body.hasMore === 'boolean'
    && body.items.every(isListItem)
  ) {
    return {
      items: body.items.map(toListItem),
      page: body.page,
      pageSize: body.pageSize,
      hasMore: body.hasMore,
    }
  }
  throw new ApiError('The server returned an invalid entries response.', response.status)
}

export async function getVaultEntry(accessToken: string, entryId: string): Promise<VaultEntry> {
  const response = await request(`${endpoint()}/${encodeURIComponent(entryId)}`, accessToken, 'GET')
  const body: unknown = await readJson(response)
  if (isEntry(body)) {
    return toEntry(body)
  }
  throw new ApiError('The server returned an invalid entry response.', response.status)
}

export async function createVaultEntry(accessToken: string, input: VaultEntryInput): Promise<VaultEntry> {
  const response = await request(`${endpoint()}`, accessToken, 'POST', input)
  const body: unknown = await readJson(response)
  if (isEntry(body)) {
    return toEntry(body)
  }
  throw new ApiError('The server returned an invalid entry response.', response.status)
}

export async function updateVaultEntry(
  accessToken: string,
  entryId: string,
  input: VaultEntryInput,
): Promise<VaultEntry> {
  const response = await request(`${endpoint()}/${encodeURIComponent(entryId)}`, accessToken, 'PUT', input)
  const body: unknown = await readJson(response)
  if (isEntry(body)) {
    return toEntry(body)
  }
  throw new ApiError('The server returned an invalid entry response.', response.status)
}

export async function deleteVaultEntry(accessToken: string, entryId: string): Promise<void> {
  await request(`${endpoint()}/${encodeURIComponent(entryId)}`, accessToken, 'DELETE')
}

async function request(
  url: string,
  accessToken: string,
  method: string,
  body?: VaultEntryInput,
): Promise<Response> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers: {
        Authorization: `Bearer ${accessToken}`,
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      ...(body ? { body: JSON.stringify(body) } : {}),
    })
  } catch {
    throw new ApiError('Unable to connect to the server. Check your connection and try again.', 0)
  }

  if (!response.ok) {
    if (response.status === 400) {
      throw new ApiError('Please review the entry details and try again.', response.status)
    }
    if (response.status === 404) {
      throw new ApiError('The requested entry is unavailable.', response.status)
    }
    if (response.status === 413) {
      throw new ApiError('The entry is too large to save.', response.status)
    }
    throw new ApiError('Unable to complete the entry request. Please try again.', response.status)
  }
  return response
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiError('The server returned an invalid entry response.', response.status)
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isListItem(value: unknown): value is VaultEntryListItem {
  return isObject(value)
    && typeof value.id === 'string'
    && typeof value.title === 'string'
    && (typeof value.websiteUrl === 'string' || value.websiteUrl === null)
    && typeof value.username === 'string'
    && typeof value.createdAt === 'string'
    && typeof value.updatedAt === 'string'
}

function isEntry(value: unknown): value is VaultEntry {
  return isListItem(value)
    && 'notes' in value
    && (typeof value.notes === 'string' || value.notes === null)
}

function toListItem(value: VaultEntryListItem): VaultEntryListItem {
  return {
    id: value.id,
    title: value.title,
    websiteUrl: value.websiteUrl,
    username: value.username,
    createdAt: value.createdAt,
    updatedAt: value.updatedAt,
  }
}

function toEntry(value: VaultEntry): VaultEntry {
  return {
    ...toListItem(value),
    notes: value.notes,
  }
}
