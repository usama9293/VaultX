import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/auth'
import {
  createVaultEntry,
  deleteVaultEntry,
  getVaultEntries,
  getVaultEntry,
  updateVaultEntry,
  type VaultEntry,
  type VaultEntryListItem,
} from '../api/vaultEntries'
import { getCurrentVault, initializeVault, type VaultResponse } from '../api/vault'
import { useAuth } from '../context/useAuth'
import './VaultDashboard.css'

type DashboardState = 'loading' | 'uninitialized' | 'ready' | 'error'
type EntryFormMode = 'create' | 'edit' | null
type EntryFormValues = {
  title: string
  websiteUrl: string
  username: string
  notes: string
}

const emptyForm: EntryFormValues = {
  title: '',
  websiteUrl: '',
  username: '',
  notes: '',
}

export function VaultDashboard({ accessToken }: { accessToken: string }) {
  const { logout } = useAuth()
  const [state, setState] = useState<DashboardState>('loading')
  const [vault, setVault] = useState<VaultResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isInitializing, setIsInitializing] = useState(false)
  const [isLoggingOut, setIsLoggingOut] = useState(false)
  const [entries, setEntries] = useState<VaultEntryListItem[]>([])
  const [searchInput, setSearchInput] = useState('')
  const [activeSearch, setActiveSearch] = useState('')
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [entryError, setEntryError] = useState<string | null>(null)
  const [entryLoading, setEntryLoading] = useState(false)
  const [selectedEntry, setSelectedEntry] = useState<VaultEntry | null>(null)
  const [formMode, setFormMode] = useState<EntryFormMode>(null)
  const [form, setForm] = useState<EntryFormValues>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)

  const loadEntries = useCallback(async (query: string, pageNumber = 1) => {
    setEntryLoading(true)
    setEntryError(null)
    try {
      const result = await getVaultEntries(accessToken, query, pageNumber)
      setEntries(result.items)
      setPage(result.page)
      setHasMore(result.hasMore)
    } catch (requestError) {
      setEntryError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to load entries. Please try again.')
    } finally {
      setEntryLoading(false)
    }
  }, [accessToken])

  const loadVault = useCallback(async () => {
    const currentVault = await getCurrentVault(accessToken)
    if (!currentVault) {
      setVault(null)
      setState('uninitialized')
      setEntries([])
      return
    }
    setVault(currentVault)
    await loadEntries('', 1)
    setState('ready')
  }, [accessToken, loadEntries])

  useEffect(() => {
    let active = true
    void getCurrentVault(accessToken)
      .then(async currentVault => {
        if (!active) return
        if (!currentVault) {
          setState('uninitialized')
          return
        }
        setVault(currentVault)
        const result = await getVaultEntries(accessToken)
        if (active) {
          setEntries(result.items)
          setHasMore(result.hasMore)
          setPage(result.page)
          setState('ready')
        }
      })
      .catch(requestError => {
        if (active) {
          setState('error')
          setError(requestError instanceof ApiError
            ? requestError.message
            : 'Unable to load your vault. Please try again.')
        }
      })
    return () => {
      active = false
    }
  }, [accessToken])

  const handleInitialize = async () => {
    setIsInitializing(true)
    setError(null)
    try {
      setVault(await initializeVault(accessToken))
      setState('ready')
      await loadEntries('', 1)
    } catch (requestError) {
      setState('error')
      setError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to initialize your vault. Please try again.')
    } finally {
      setIsInitializing(false)
    }
  }

  const handleLogout = async () => {
    setIsLoggingOut(true)
    setEntries([])
    setSelectedEntry(null)
    setForm(emptyForm)
    setFormMode(null)
    setSearchInput('')
    setActiveSearch('')
    setEntryError(null)
    setFormError(null)
    try {
      await logout()
    } catch {
      // AuthProvider clears local state even when server logout fails.
    } finally {
      setIsLoggingOut(false)
    }
  }

  const openEntry = async (entryId: string) => {
    setEntryLoading(true)
    setEntryError(null)
    try {
      setSelectedEntry(await getVaultEntry(accessToken, entryId))
      setFormMode(null)
    } catch (requestError) {
      setEntryError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to open this entry. Please try again.')
    } finally {
      setEntryLoading(false)
    }
  }

  const startCreate = () => {
    setSelectedEntry(null)
    setForm(emptyForm)
    setFormError(null)
    setFormMode('create')
  }

  const startEdit = () => {
    if (!selectedEntry) return
    setForm({
      title: selectedEntry.title,
      websiteUrl: selectedEntry.websiteUrl ?? '',
      username: selectedEntry.username,
      notes: selectedEntry.notes ?? '',
    })
    setFormError(null)
    setFormMode('edit')
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const validationMessage = validateEntry(form)
    if (validationMessage) {
      setFormError(validationMessage)
      return
    }

    setIsSaving(true)
    setFormError(null)
    const input = {
      title: form.title.trim().normalize('NFC'),
      websiteUrl: form.websiteUrl.trim() ? form.websiteUrl.trim().normalize('NFC') : null,
      username: form.username.trim().normalize('NFC'),
      notes: form.notes.trim() ? form.notes.normalize('NFC') : null,
    }
    try {
      const savedEntry = formMode === 'edit' && selectedEntry
        ? await updateVaultEntry(accessToken, selectedEntry.id, input)
        : await createVaultEntry(accessToken, input)
      setSelectedEntry(savedEntry)
      setFormMode(null)
      setActiveSearch('')
      setSearchInput('')
      await loadEntries('', 1)
    } catch (requestError) {
      setFormError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to save this entry. Please try again.')
    } finally {
      setIsSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!selectedEntry || !window.confirm(`Delete "${selectedEntry.title}"? This cannot be undone.`)) {
      return
    }
    setEntryLoading(true)
    setEntryError(null)
    try {
      await deleteVaultEntry(accessToken, selectedEntry.id)
      setSelectedEntry(null)
      await loadEntries(activeSearch, page)
    } catch (requestError) {
      setEntryError(requestError instanceof ApiError
        ? requestError.message
        : 'Unable to delete this entry. Please try again.')
    } finally {
      setEntryLoading(false)
    }
  }

  const handleSearch = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextSearch = searchInput.trim()
    if (scalarCount(nextSearch) > 128 || containsUnsupportedControl(nextSearch)) {
      setEntryError('Search query must be 128 characters or fewer and contain no control characters.')
      return
    }
    setActiveSearch(nextSearch)
    await loadEntries(nextSearch, 1)
  }

  const handlePageChange = async (nextPage: number) => {
    await loadEntries(activeSearch, nextPage)
  }

  return (
    <section className="vault-dashboard" role="region" aria-label="Authenticated Session">
      <header className="vault-dashboard-header">
        <div>
          <div className="brand-badge">VaultX Security</div>
          <h1 className="form-title">Your Vault</h1>
        </div>
        <button
          type="button"
          className="link-btn"
          onClick={handleLogout}
          disabled={isLoggingOut}
          aria-busy={isLoggingOut}
        >
          {isLoggingOut ? 'Signing Out...' : 'Log Out'}
        </button>
      </header>

      {state === 'loading' && (
        <div className="vault-dashboard-state" role="status" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          <span>Loading your vault...</span>
        </div>
      )}

      {state === 'uninitialized' && (
        <div className="vault-dashboard-state">
          <h2>Set up your vault</h2>
          <p>Your personal vault is ready to be initialized.</p>
          <button
            type="button"
            className="submit-btn"
            onClick={handleInitialize}
            disabled={isInitializing}
            aria-busy={isInitializing}
          >
            {isInitializing ? 'Initializing...' : 'Initialize Vault'}
          </button>
        </div>
      )}

      {state === 'ready' && vault && (
        <div className="vault-entries">
          <p className="metadata-notice" role="status">
            Phase 4 stores entry metadata only. Passwords are not stored yet.
          </p>

          {entryError && <div className="form-status-alert error" role="alert">{entryError}</div>}

          {formMode && (
            <form className="entry-form" onSubmit={handleSubmit} noValidate>
              <div className="entry-view-heading">
                <h2>{formMode === 'create' ? 'Add Entry' : 'Edit Entry'}</h2>
                <button type="button" className="link-btn" onClick={() => setFormMode(null)}>
                  Cancel
                </button>
              </div>
              <label>
                Title
                <input
                  autoComplete="off"
                  maxLength={510}
                  value={form.title}
                  onChange={event => setForm({ ...form, title: event.target.value })}
                  required
                />
              </label>
              <label>
                Website URL (optional)
                <input
                  autoComplete="url"
                  inputMode="url"
                  maxLength={4096}
                  type="url"
                  value={form.websiteUrl}
                  onChange={event => setForm({ ...form, websiteUrl: event.target.value })}
                />
              </label>
              <label>
                Username
                <input
                  autoComplete="username"
                  maxLength={510}
                  value={form.username}
                  onChange={event => setForm({ ...form, username: event.target.value })}
                  required
                />
              </label>
              <label>
                Notes (optional)
                <textarea
                  maxLength={4000}
                  rows={4}
                  value={form.notes}
                  onChange={event => setForm({ ...form, notes: event.target.value })}
                />
              </label>
              {formError && <div className="form-status-alert error" role="alert">{formError}</div>}
              <button type="submit" className="submit-btn" disabled={isSaving} aria-busy={isSaving}>
                {isSaving ? 'Saving...' : formMode === 'create' ? 'Create Entry' : 'Save Changes'}
              </button>
            </form>
          )}

          {!formMode && selectedEntry && (
            <article className="entry-detail">
              <div className="entry-view-heading">
                <h2>{selectedEntry.title}</h2>
                <button type="button" className="link-btn" onClick={() => setSelectedEntry(null)}>
                  Back to entries
                </button>
              </div>
              <dl>
                <dt>Username</dt>
                <dd>{selectedEntry.username}</dd>
                <dt>Website</dt>
                <dd>{renderWebsite(selectedEntry.websiteUrl)}</dd>
                <dt>Notes</dt>
                <dd className="entry-notes">{selectedEntry.notes || 'No notes'}</dd>
                <dt>Created</dt>
                <dd>{new Date(selectedEntry.createdAt).toLocaleString()}</dd>
                <dt>Updated</dt>
                <dd>{new Date(selectedEntry.updatedAt).toLocaleString()}</dd>
              </dl>
              <div className="entry-actions">
                <button type="button" className="submit-btn" onClick={startEdit}>Edit</button>
                <button type="button" className="danger-btn" onClick={handleDelete} disabled={entryLoading}>
                  Delete
                </button>
              </div>
            </article>
          )}

          {!formMode && !selectedEntry && (
            <>
              <div className="entry-view-heading">
                <h2>Entries</h2>
                <button type="button" className="submit-btn" onClick={startCreate}>Add Entry</button>
              </div>
              <form className="entry-search" onSubmit={handleSearch}>
                <label htmlFor="entry-search">Search title, website, or username</label>
                <div>
                  <input
                    id="entry-search"
                    maxLength={256}
                    value={searchInput}
                    onChange={event => setSearchInput(event.target.value)}
                  />
                  <button type="submit" className="submit-btn" disabled={entryLoading}>Search</button>
                  {activeSearch && (
                    <button
                      type="button"
                      className="link-btn"
                      onClick={() => {
                        setSearchInput('')
                        setActiveSearch('')
                        void loadEntries('', 1)
                      }}
                    >
                      Clear
                    </button>
                  )}
                </div>
              </form>
              {entryLoading ? (
                <div className="entry-list-state" role="status">Loading entries...</div>
              ) : entries.length === 0 ? (
                <div className="entry-list-state">
                  <h3>{activeSearch ? 'No matching entries' : 'No entries yet'}</h3>
                  <p>{activeSearch ? 'Try a different search.' : 'Create an entry to save metadata for an account.'}</p>
                </div>
              ) : (
                <ul className="entry-list" aria-label="Vault entries">
                  {entries.map(entry => (
                    <li key={entry.id}>
                      <button type="button" className="entry-list-item" onClick={() => void openEntry(entry.id)}>
                        <span className="entry-list-title">{entry.title}</span>
                        <span>{entry.username}</span>
                        {entry.websiteUrl && <span className="entry-list-url">{entry.websiteUrl}</span>}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <nav className="entry-pagination" aria-label="Entry pages">
                <button type="button" className="link-btn" disabled={page <= 1 || entryLoading} onClick={() => void handlePageChange(page - 1)}>
                  Previous
                </button>
                <span>Page {page}</span>
                <button type="button" className="link-btn" disabled={!hasMore || entryLoading} onClick={() => void handlePageChange(page + 1)}>
                  Next
                </button>
              </nav>
            </>
          )}
        </div>
      )}

      {state === 'error' && (
        <div className="vault-dashboard-state">
          <div className="form-status-alert error" role="alert" aria-live="assertive">
            {error}
          </div>
          <button
            type="button"
            className="submit-btn"
            onClick={() => {
              setState('loading')
              setError(null)
              void loadVault().catch(requestError => {
                setState('error')
                setError(requestError instanceof ApiError
                  ? requestError.message
                  : 'Unable to load your vault. Please try again.')
              })
            }}
          >
            Retry
          </button>
        </div>
      )}
    </section>
  )
}

function validateEntry(input: EntryFormValues): string | null {
  const title = input.title.trim()
  const username = input.username.trim()
  if (!title || !username) return 'Title and username are required.'
  if (scalarCount(title) > 255 || scalarCount(username) > 255) {
    return 'Title and username must be 255 characters or fewer.'
  }
  if (containsUnsupportedControl(title) || containsUnsupportedControl(username)) {
    return 'Title and username cannot contain control characters.'
  }
  if (scalarCount(input.notes) > 2000 || containsUnsupportedControl(input.notes, true)) {
    return 'Notes are too long or contain unsupported characters.'
  }
  const websiteUrl = input.websiteUrl.trim()
  if (websiteUrl) {
    try {
      const url = new URL(websiteUrl)
      if (
        !['http:', 'https:'].includes(url.protocol)
        || !url.hostname
        || url.username
        || url.password
        || scalarCount(websiteUrl) > 2048
        || containsUnsupportedControl(websiteUrl)
      ) {
        return 'Website URL must be a valid HTTP or HTTPS address.'
      }
    } catch {
      return 'Website URL must be a valid HTTP or HTTPS address.'
    }
  }
  return null
}

function scalarCount(value: string): number {
  return Array.from(value).length
}

function containsUnsupportedControl(value: string, allowWhitespace = false): boolean {
  return Array.from(value).some(character => {
    const code = character.codePointAt(0) ?? 0
    const isControl = code <= 0x1f || (code >= 0x7f && code <= 0x9f)
    return isControl && !(allowWhitespace && [0x09, 0x0a, 0x0d].includes(code))
  })
}

function renderWebsite(value: string | null) {
  if (!value) return 'No website'
  try {
    const url = new URL(value)
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) {
      return value
    }
    return <a href={url.href} target="_blank" rel="noopener noreferrer">{value}</a>
  } catch {
    return value
  }
}
