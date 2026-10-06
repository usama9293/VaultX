import { test, expect, type Page } from '../../frontend/node_modules/@playwright/test'
import { createE2EUser } from '../fixtures/test-user'

async function login(page: Page, email: string, password: string) {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
  await page.getByLabel('Email Address').fill(email)
  await page.getByRole('textbox', { name: 'Master Password' }).fill(password)
  const responsePromise = page.waitForResponse(
    response => response.url().endsWith('/api/auth/login') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Sign In to VaultX' }).click()
  const response = await responsePromise
  expect(response.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Your Vault' })).toBeVisible()
}

test.describe('Vault browser E2E', () => {
  test('authenticated user initializes once and retrieves the same empty vault after reload', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await login(page, user.email, user.password)

    await expect(page.getByText('Set up your vault')).toBeVisible()
    const createResponsePromise = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Initialize Vault' }).click()
    const createResponse = await createResponsePromise
    expect(createResponse.status()).toBe(201)
    const createdVault = await createResponse.json()
    await expect(page.getByText('No passwords yet')).toBeVisible()

    const getResponsePromise = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'GET',
    )
    await page.reload()
    const getResponse = await getResponsePromise
    expect(getResponse.status()).toBe(200)
    expect((await getResponse.json()).id).toBe(createdVault.id)
    await expect(page.getByText('No passwords yet')).toBeVisible()
  })

  test('each authenticated user sees only their own vault', async ({ page, request }) => {
    const userA = await createE2EUser(request)
    await login(page, userA.email, userA.password)
    const createA = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Initialize Vault' }).click()
    const vaultA = await (await createA).json()

    const logout = page.waitForResponse(
      response => response.url().endsWith('/api/auth/logout') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Log Out' }).click()
    expect((await logout).status()).toBe(204)

    const userB = await createE2EUser(request)
    await login(page, userB.email, userB.password)
    const createB = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Initialize Vault' }).click()
    const vaultB = await (await createB).json()
    expect(vaultA.id).not.toBe(vaultB.id)
    await expect(page.getByText('No passwords yet')).toBeVisible()
  })

  test('unauthenticated visitor cannot access the dashboard', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Vault' })).toHaveCount(0)
  })
})
