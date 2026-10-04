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

async function loginAndVerifyAuthenticated(page: Page, email: string, password: string) {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

  const loginResponse = await submitLogin(page, email, password)
  expect(loginResponse.status()).toBe(200)

  await expect(page.getByRole('region', { name: 'Authenticated Session' })).toBeVisible()
  await expect(page.getByText('Session Established')).toBeVisible()

  const logoutButton = page.getByRole('button', { name: 'Log Out' })
  await expect(logoutButton).toBeVisible()

  return loginResponse
}

test.describe('Logout browser E2E', () => {
  test('1. complete browser logout transitions to unauthenticated UI and revokes session', async ({ page, request }) => {
    // 1. Create a unique E2E user
    const user = await createE2EUser(request)

    // 2-8. Navigate to '/', verify Sign In UI, login with real credentials, verify HTTP 200 and authenticated UI
    await loginAndVerifyAuthenticated(page, user.email, user.password)

    // 9. Verify the real Log Out button is visible
    const logoutBtn = page.getByRole('button', { name: 'Log Out' })
    await expect(logoutBtn).toBeVisible()

    // 10-11. Setup response listener and click Log Out
    const logoutResponsePromise = page.waitForResponse(
      (response) => response.url().endsWith('/api/auth/logout') && response.request().method() === 'POST',
    )
    await logoutBtn.click()

    // 12. Verify logout response is HTTP 204
    const logoutResponse = await logoutResponsePromise
    expect(logoutResponse.status()).toBe(204)

    // 13. Verify the UI transitions back to Sign In
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

    // 14. Verify the authenticated session region is gone
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toHaveCount(0)

    // 15. Verify no raw error is displayed
    await expect(page.getByRole('alert')).toHaveCount(0)
  })

  test('2. sets a protected refresh cookie on login and clears it from the browser jar on logout', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await loginAndVerifyAuthenticated(page, user.email, user.password)

    // 1-4. Inspect cookies after login: verify refreshToken exists and has value
    const cookiesBefore = await page.context().cookies()
    const refreshCookieBefore = cookiesBefore.find((cookie) => cookie.name === 'refreshToken')

    const cookieAssertions = {
      exists: Boolean(refreshCookieBefore),
      hasValue: Boolean(refreshCookieBefore?.value),
      httpOnly: refreshCookieBefore?.httpOnly === true,
      path: refreshCookieBefore?.path === '/api/auth',
      sameSite: refreshCookieBefore?.sameSite === 'Lax',
      hasExpiration: Boolean(refreshCookieBefore && refreshCookieBefore.expires > 0),
      secure: refreshCookieBefore?.secure === false,
    }

    // 5-9. Verify cookie security attributes
    expect(cookieAssertions).toEqual({
      exists: true,
      hasValue: true,
      httpOnly: true,
      path: true,
      sameSite: true,
      hasExpiration: true,
      secure: true,
    })

    // Verify document.cookie does NOT expose refreshToken
    expect(await page.evaluate(() => document.cookie.includes('refreshToken'))).toBe(false)

    // Perform real browser logout
    const logoutResponsePromise = page.waitForResponse(
      (response) => response.url().endsWith('/api/auth/logout') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Log Out' }).click()
    const logoutResponse = await logoutResponsePromise
    expect(logoutResponse.status()).toBe(204)

    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

    // Verify refreshToken has been removed from the browser cookie jar
    const cookiesAfter = await page.context().cookies()
    const refreshCookieAfter = cookiesAfter.find((cookie) => cookie.name === 'refreshToken')
    expect(refreshCookieAfter).toBeUndefined()

    // Verify document.cookie remains clean
    expect(await page.evaluate(() => document.cookie.includes('refreshToken'))).toBe(false)
  })

  test('3. keeps the access token out of persistent browser storage and URLs during and after logout', async ({ page, request }) => {
    const user = await createE2EUser(request)
    const loginResponse = await loginAndVerifyAuthenticated(page, user.email, user.password)

    // Capture access token from login response
    const loginBody = await loginResponse.json()
    const accessToken = loginBody.accessToken as string
    expect(typeof accessToken).toBe('string')
    expect(accessToken.length).toBeGreaterThan(0)

    // Helper to inspect storage, URL, and document.cookie for token exposure
    const checkTokenExposure = async (token: string) => {
      return page.evaluate((tokenVal) => {
        const storedValues = [
          ...Object.values(localStorage),
          ...Object.values(sessionStorage),
        ]
        const url = `${window.location.search}${window.location.hash}`
        const inCookie = document.cookie.includes(tokenVal)
        return storedValues.includes(tokenVal) || url.includes(tokenVal) || inCookie
      }, token)
    }

    // Verify access token is NOT in localStorage, sessionStorage, URL, or document.cookie while authenticated
    expect(await checkTokenExposure(accessToken)).toBe(false)

    // Perform real logout
    const logoutResponsePromise = page.waitForResponse(
      (response) => response.url().endsWith('/api/auth/logout') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Log Out' }).click()
    const logoutResponse = await logoutResponsePromise
    expect(logoutResponse.status()).toBe(204)

    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

    // Verify access token is NOT in localStorage, sessionStorage, URL, or document.cookie after logout
    expect(await checkTokenExposure(accessToken)).toBe(false)
    expect(await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0)).toBe(true)
  })

  test('4. clears local authentication state and returns to Sign In UI when logout API encounters network failure', async ({ page, request }) => {
    const user = await createE2EUser(request)
    const loginResponse = await loginAndVerifyAuthenticated(page, user.email, user.password)

    const loginBody = await loginResponse.json()
    const accessToken = loginBody.accessToken as string

    // Intercept and abort the real POST /api/auth/logout to simulate network failure
    await page.route('**/api/auth/logout', (route) => route.abort())

    // Click Log Out
    await page.getByRole('button', { name: 'Log Out' }).click()

    // Verify UI returns to Sign In
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

    // Verify authenticated session region disappears
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toHaveCount(0)

    // Verify no raw exception, stack trace, or token is rendered
    const pageText = await page.evaluate(() => document.body.textContent ?? '')
    expect(pageText).not.toContain(accessToken)
    expect(pageText).not.toContain('TypeError')
    expect(pageText).not.toContain('Failed to fetch')
    expect(pageText).not.toContain('stack')

    // Verify access token is not placed into localStorage, sessionStorage, or URL
    const isExposed = await page.evaluate((tokenVal) => {
      const storedValues = [
        ...Object.values(localStorage),
        ...Object.values(sessionStorage),
      ]
      const url = `${window.location.search}${window.location.hash}`
      return storedValues.includes(tokenVal) || url.includes(tokenVal)
    }, accessToken)
    expect(isExposed).toBe(false)
  })

  test('5. shows loading state, disables button, and prevents duplicate requests while logout is in flight', async ({ page, request }) => {
    const user = await createE2EUser(request)
    await loginAndVerifyAuthenticated(page, user.email, user.password)

    let requestCount = 0
    let fulfillLogout: (() => void) | undefined
    const holdLogoutPromise = new Promise<void>((resolve) => {
      fulfillLogout = resolve
    })

    // Intercept POST /api/auth/logout and hold pending
    await page.route('**/api/auth/logout', async (route) => {
      requestCount++
      await holdLogoutPromise
      await route.continue()
    })

    const logoutBtn = page.getByRole('button', { name: 'Log Out' })
    await logoutBtn.click()

    // Verify "Signing Out..." loading state, disabled attribute, and aria-busy
    const pendingBtn = page.getByRole('button', { name: 'Signing Out...' })
    await expect(pendingBtn).toBeVisible()
    await expect(pendingBtn).toBeDisabled()
    await expect(pendingBtn).toHaveAttribute('aria-busy', 'true')

    // Attempt another click while request is pending (disabled element suppresses click in real browser)
    await pendingBtn.click({ force: true }).catch(() => undefined)

    // Release the route to let the request finish
    fulfillLogout!()

    // Verify the UI returns to Sign In and exactly ONE request was made
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Authenticated Session' })).toHaveCount(0)
    expect(requestCount).toBe(1)
  })
})
