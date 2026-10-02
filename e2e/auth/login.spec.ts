import { test, expect, type Page } from '../../frontend/node_modules/@playwright/test'
import { createE2EUser } from '../fixtures/test-user'

async function submitLogin(page: Page, email: string, password: string) {
  await page.getByLabel('Email Address').fill(email)
  await page.getByRole('textbox', { name: 'Master Password' }).fill(password)

  const responsePromise = page.waitForResponse(
    (response) => response.url().endsWith('/api/auth/login') && response.request().method() === 'POST',
  )

  await page.getByRole('button', { name: 'Sign In to VaultX' }).click()
  return responsePromise
}

test.describe('Login browser E2E', () => {
  test('valid login uses the real API and displays the authenticated UI', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    const response = await submitLogin(page, user.email, user.password)

    expect(response.status()).toBe(200)
    const body = await response.json()
    expect(typeof body.accessToken).toBe('string')
    expect(body.accessToken.length).toBeGreaterThan(0)
    expect(typeof body.expiresAt).toBe('string')
    expect(body.expiresAt.length).toBeGreaterThan(0)

    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toBeVisible()
    await expect(page.getByText('Session Established')).toBeVisible()
  })

  test('sets a protected refresh cookie without exposing it to JavaScript', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await page.goto('/')

    await submitLogin(page, user.email, user.password)
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toBeVisible()

    const refreshCookie = (await page.context().cookies()).find((cookie) => cookie.name === 'refreshToken')
    const cookieAssertions = {
      exists: Boolean(refreshCookie),
      hasValue: Boolean(refreshCookie?.value),
      httpOnly: refreshCookie?.httpOnly === true,
      path: refreshCookie?.path === '/api/auth',
      sameSite: refreshCookie?.sameSite === 'Lax',
      hasExpiration: Boolean(refreshCookie && refreshCookie.expires > 0),
      secure: refreshCookie?.secure === false,
    }

    expect(cookieAssertions).toEqual({
      exists: true,
      hasValue: true,
      httpOnly: true,
      path: true,
      sameSite: true,
      hasExpiration: true,
      secure: true,
    })
    expect(await page.evaluate(() => document.cookie.includes('refreshToken'))).toBe(false)
  })

  test('keeps the access token out of persistent browser storage and URLs', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await page.goto('/')

    const response = await submitLogin(page, user.email, user.password)
    const body = await response.json()
    const accessToken = body.accessToken as string

    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toBeVisible()

    const accessTokenExposure = await page.evaluate((token) => {
      const storedValues = [
        ...Object.values(localStorage),
        ...Object.values(sessionStorage),
      ]
      const url = `${window.location.search}${window.location.hash}`
      return storedValues.includes(token) || url.includes(token)
    }, accessToken)

    expect(accessTokenExposure).toBe(false)
    expect(await page.evaluate(() => document.cookie.includes('refreshToken'))).toBe(false)
  })

  test('shows the generic error for an invalid password', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await page.goto('/')

    const response = await submitLogin(page, user.email, 'Wrong-E2E-Password-2026!')
    expect(response.status()).toBe(401)
    await expect(page.getByRole('alert')).toHaveText('Invalid email or password.')
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toHaveCount(0)
  })

  test('shows the same generic error for an unknown email', async ({ page }) => {
    await page.goto('/')

    const response = await submitLogin(page, `unknown-${Date.now()}@vaultx.local`, 'Valid-E2E-Password-2026!')
    expect(response.status()).toBe(401)
    await expect(page.getByRole('alert')).toHaveText('Invalid email or password.')
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toHaveCount(0)
  })

  test('rejects empty and malformed login input in the real browser UI', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Sign In to VaultX' }).click()
    await expect(page.getByText('Email is required.')).toBeVisible()
    await expect(page.getByText('Password is required.')).toBeVisible()

    await page.getByLabel('Email Address').fill('not-an-email')
    await page.getByRole('textbox', { name: 'Master Password' }).fill('some-password')
    await page.getByRole('button', { name: 'Sign In to VaultX' }).click()
    await expect(page.getByText('Email format is invalid.')).toBeVisible()
  })

  test('shows the safe network failure message when login is unavailable', async ({ page }) => {
    await page.route('**/api/auth/login', (route) => route.abort())
    await page.goto('/')

    await page.getByLabel('Email Address').fill('network.failure@vaultx.local')
    await page.getByRole('textbox', { name: 'Master Password' }).fill('Valid-E2E-Password-2026!')
    await page.getByRole('button', { name: 'Sign In to VaultX' }).click()
    await expect(page.getByRole('alert')).toHaveText(
      'Unable to connect to the server. Please check your connection and try again.',
    )
  })
})
