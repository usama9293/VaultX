import { ApiError } from './auth'

export interface VaultResponse {
  id: string
  createdAt: string
  updatedAt: string
}

export async function getCurrentVault(accessToken: string): Promise<VaultResponse | null> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  let response: Response
  try {
    response = await fetch(`${baseUrl}/api/vault`, {
      method: 'GET',
      headers: { Authorization: `Bearer ${accessToken}` },
    })
  } catch {
    throw new ApiError('Unable to load your vault. Check your connection and try again.', 0)
  }

  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new ApiError('Unable to load your vault. Please try again.', response.status)
  }

  return readVaultResponse(response)
}

export async function initializeVault(accessToken: string): Promise<VaultResponse> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  let response: Response
  try {
    response = await fetch(`${baseUrl}/api/vault`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${accessToken}` },
    })
  } catch {
    throw new ApiError('Unable to initialize your vault. Check your connection and try again.', 0)
  }

  if (!response.ok) {
    throw new ApiError('Unable to initialize your vault. Please try again.', response.status)
  }

  return readVaultResponse(response)
}

async function readVaultResponse(response: Response): Promise<VaultResponse> {
  try {
    const body: unknown = await response.json()
    if (
      typeof body === 'object'
      && body !== null
      && 'id' in body
      && 'createdAt' in body
      && 'updatedAt' in body
      && typeof body.id === 'string'
      && typeof body.createdAt === 'string'
      && typeof body.updatedAt === 'string'
    ) {
      return { id: body.id, createdAt: body.createdAt, updatedAt: body.updatedAt }
    }
  } catch {
    throw new ApiError('The server returned an invalid vault response.', response.status)
  }

  throw new ApiError('The server returned an invalid vault response.', response.status)
}
