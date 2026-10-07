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
    await expect(page.getByText('No entries yet')).toBeVisible()

    const getResponsePromise = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'GET',
    )
    await page.reload()
    const getResponse = await getResponsePromise
    expect(getResponse.status()).toBe(200)
    expect((await getResponse.json()).id).toBe(createdVault.id)
    await expect(page.getByText('No entries yet')).toBeVisible()
  })

  test('each authenticated user sees only their own vault', async ({ page, request }) => {
    const userA = await createE2EUser(request)
    await login(page, userA.email, userA.password)
    const createA = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Initialize Vault' }).click()
    const vaultA = await (await createA).json()
    await page.getByRole('button', { name: 'Add Entry' }).click()
    await page.getByLabel('Title').fill('A private entry')
    await page.getByLabel('Username').fill('owner-a')
    const createEntryA = page.waitForResponse(
      response => response.url().endsWith('/api/vault/entries') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Create Entry' }).click()
    expect((await createEntryA).status()).toBe(201)
    await expect(page.getByRole('heading', { name: 'A private entry' })).toBeVisible()

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
    await expect(page.getByText('No entries yet')).toBeVisible()
    await page.getByRole('textbox', { name: /Search title/ }).fill('private')
    const noForeignSearch = page.waitForResponse(
      response => new URL(response.url()).searchParams.get('q') === 'private',
    )
    await page.getByRole('button', { name: 'Search' }).click()
    expect((await noForeignSearch).status()).toBe(200)
    await expect(page.getByText('No matching entries')).toBeVisible()
  })

  test('creates, views, edits, searches, deletes and clears metadata on logout', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await login(page, user.email, user.password)

    await expect(page.getByText('Set up your vault')).toBeVisible()
    const initialize = page.waitForResponse(
      response => response.url().endsWith('/api/vault') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Initialize Vault' }).click()
    expect((await initialize).status()).toBe(201)

    await expect(page.getByText('Phase 4 stores entry metadata only. Passwords are not stored yet.')).toBeVisible()
    await page.getByRole('button', { name: 'Add Entry' }).click()
    await page.getByLabel('Title').fill('E2E account')
    await page.getByLabel('Website URL (optional)').fill('https://example.test')
    await page.getByLabel('Username').fill('e2e-user@example.test')
    await page.getByLabel('Notes (optional)').fill('Metadata notes')

    const createResponsePromise = page.waitForResponse(
      response => response.url().endsWith('/api/vault/entries') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Create Entry' }).click()
    const createResponse = await createResponsePromise
    expect(createResponse.status()).toBe(201)
    const created = await createResponse.json()
    expect(created).not.toHaveProperty('password')
    expect(created).not.toHaveProperty('vaultId')
    await expect(page.getByRole('heading', { name: 'E2E account' })).toBeVisible()
    await expect(page.getByText('Metadata notes')).toBeVisible()

    await page.getByRole('button', { name: 'Edit' }).click()
    await page.getByLabel('Title').fill('E2E updated account')
    const updateResponsePromise = page.waitForResponse(
      response => response.url().includes('/api/vault/entries/') && response.request().method() === 'PUT',
    )
    await page.getByRole('button', { name: 'Save Changes' }).click()
    expect((await updateResponsePromise).status()).toBe(200)
    await expect(page.getByRole('heading', { name: 'E2E updated account' })).toBeVisible()

    await page.getByRole('button', { name: 'Back to entries' }).click()
    await page.getByRole('textbox', { name: /Search title/ }).fill('updated')
    const searchResponsePromise = page.waitForResponse(
      response => {
        if (!response.url().includes('/api/vault/entries')) return false
        return new URL(response.url()).searchParams.get('q') === 'updated'
      },
    )
    await page.getByRole('button', { name: 'Search' }).click()
    expect((await searchResponsePromise).status()).toBe(200)
    await page.getByRole('button', { name: /E2E updated account/ }).click()

    page.once('dialog', dialog => dialog.accept())
    const deleteResponsePromise = page.waitForResponse(
      response => response.url().includes('/api/vault/entries/') && response.request().method() === 'DELETE',
    )
    await page.getByRole('button', { name: 'Delete' }).click()
    expect((await deleteResponsePromise).status()).toBe(204)

    const logoutResponsePromise = page.waitForResponse(
      response => response.url().endsWith('/api/auth/logout') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Log Out' }).click()
    expect((await logoutResponsePromise).status()).toBe(204)
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Vault' })).toHaveCount(0)
  })

  test('unauthenticated visitor cannot access the dashboard', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Vault' })).toHaveCount(0)
  })
})
