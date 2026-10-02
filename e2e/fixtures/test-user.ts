import { randomUUID } from 'node:crypto'
import type { APIRequestContext } from '../../frontend/node_modules/@playwright/test'

const testPassword = 'E2E-Test-Password-2026!'

export type E2EUser = {
  email: string
  password: string
}

export async function createE2EUser(request: APIRequestContext): Promise<E2EUser> {
  const user = {
    email: `e2e-${randomUUID()}@vaultx.local`,
    password: testPassword,
  }

  const response = await request.post('http://localhost:5071/api/auth/register', {
    data: {
      email: user.email,
      password: user.password,
      confirmPassword: user.password,
    },
  })

  if (response.status() !== 201) {
    throw new Error(`E2E test-user registration failed with status ${response.status()}.`)
  }

  return user
}
