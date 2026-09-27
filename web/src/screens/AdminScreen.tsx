import { useCallback, useEffect, useState } from 'react'
import {
  api,
  type AdminUser,
  type AdminUserPage,
  type PendingDictionaryPage,
  type ShelfWord,
  type ShelfWordPage,
  type SortStatus,
  type UserRole,
} from '../api/client'
import { ROLES, roleLabel } from '../auth/roles'
import { formatInt, plural } from '../lib/format'
import './AdminScreen.css'

interface Props {
  /** The signed-in admin. Their own row is read-only — the server refuses those actions anyway. */
  meId: number
}

/** How long the admin has to stop typing before the search is sent. */
const searchDelayMs = 300

const dateFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium' })

function formatDate(value: string | null) {
  return value ? dateFormat.format(new Date(value)) : '—'
}

function pageCount(data: AdminUserPage) {
  return Math.max(1, Math.ceil(data.total / data.pageSize))
}

export function AdminScreen({ meId }: Props) {
  const [tab, setTab] = useState<'users' | 'dictionaries' | 'shelf'>('users')
  // `query` is what is typed, `search` is what was last sent: the two differ during the
  // debounce so a keystroke does not fire a request.
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<AdminUserPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<number | null>(null)
  const [confirmingDictionaries, setConfirmingDictionaries] = useState<number | null>(null)

  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(query.trim())
      setPage(1)
    }, searchDelayMs)

    return () => clearTimeout(handle)
  }, [query])

  const reload = useCallback(
    () =>
      api
        .listUsers({ search, page })
        .then((result) => {
          // A delete can empty the page we are on; fall back to the last page that exists
          // rather than show an empty table with a live "Previous" button.
          if (result.items.length === 0 && result.page > 1) {
            setPage(pageCount(result))
            return
          }

          setData(result)
          setError(null)
        })
        .catch((e) => setError(e instanceof Error ? e.message : String(e))),
    [search, page],
  )

  useEffect(() => {
    void reload()
  }, [reload])

  // Every action is "do it, then re-read the list": the server owns the truth about
  // roles and bans, and a refused action must leave the row exactly as it was.
  const run = useCallback(
    async (action: () => Promise<unknown>) => {
      setError(null)

      try {
        await action()
        await reload()
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
      }
    },
    [reload],
  )

  const remove = (user: AdminUser) => {
    if (confirming !== user.id) {
      setConfirming(user.id)
      return
    }

    setConfirming(null)
    void run(() => api.deleteUser(user.id))
  }

  // Used alongside a ban: wipes what the account published, not the account itself. Same
  // confirm-then-click shape as remove() above, its own state so the two prompts in a row
  // don't fight over which action is armed.
  const deleteDictionaries = (user: AdminUser) => {
    if (confirmingDictionaries !== user.id) {
      setConfirmingDictionaries(user.id)
      return
    }

    setConfirmingDictionaries(null)
    void run(() => api.deleteUserDictionaries(user.id))
  }

  const pages = data ? pageCount(data) : 1

  return (
    <section className="admin">
      <h1 className="large-title">Users</h1>

      <div className="admin-tabs">
        <button
          type="button"
          className={`btn ${tab === 'users' ? 'btn-secondary' : 'btn-quiet'}`}
          onClick={() => setTab('users')}
        >
          Users
        </button>
        <button
          type="button"
          className={`btn ${tab === 'dictionaries' ? 'btn-secondary' : 'btn-quiet'}`}
          onClick={() => setTab('dictionaries')}
        >
          Dictionaries
        </button>
        <button
          type="button"
          className={`btn ${tab === 'shelf' ? 'btn-secondary' : 'btn-quiet'}`}
          onClick={() => setTab('shelf')}
        >
          Shelf
        </button>
      </div>

      {tab === 'users' && (
        <>
          <div className="admin-toolbar">
            <label className="field admin-search">
              <input
                type="search"
                placeholder="Search by name or username"
                aria-label="Search users"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </label>

            {data && (
              <span className="footnote admin-count">
                {data.total === 0
                  ? 'No users match'
                  : `${formatInt(data.total)} ${plural(data.total, 'user', 'users')}`}
              </span>
            )}
          </div>

          {error && <p className="error">{error}</p>}
          {!data && !error && <p className="footnote">Loading…</p>}

          {data && data.items.length > 0 && (
            <table className="admin-table">
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Role</th>
                  <th scope="col">Status</th>
                  <th scope="col">Joined</th>
                  <th scope="col">Last seen</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((user) => {
                  const isMe = user.id === meId

                  return (
                    <tr key={user.id} className={user.isBanned ? 'is-banned' : undefined}>
                      <th scope="row">
                        <span className="user-name">{user.displayName}</span>
                        {user.username && <span className="caption">@{user.username}</span>}
                      </th>
                      <td className="role">
                        <label className="field role-field">
                          <select
                            className="role-select"
                            aria-label={`Role of ${user.displayName}`}
                            value={user.role}
                            disabled={isMe}
                            onChange={(e) => void run(() => api.setUserRole(user.id, e.target.value as UserRole))}
                          >
                            {ROLES.map((role) => (
                              <option key={role} value={role}>
                                {roleLabel(role)}
                              </option>
                            ))}
                          </select>
                        </label>
                      </td>
                      <td className="status">{user.isBanned ? 'Banned' : 'Active'}</td>
                      <td className="num">{formatDate(user.createdAt)}</td>
                      <td className="num">{formatDate(user.lastLoginAt)}</td>
                      <td className="actions">
                        {/* The flex row lives on a wrapper, not the cell: a `td` with `display: flex`
                            stops being a table cell, so it no longer stretches to the row height and
                            its bottom border drifts away from the neighbouring cells'. */}
                        <div className="action-buttons">
                          <button
                            type="button"
                            className="btn btn-quiet ban"
                            disabled={isMe}
                            onClick={() =>
                              void run(() => (user.isBanned ? api.unbanUser(user.id) : api.banUser(user.id)))
                            }
                          >
                            {user.isBanned ? 'Unban' : 'Ban'}
                          </button>

                          <button
                            type="button"
                            className="btn btn-quiet delete"
                            disabled={isMe}
                            onClick={() => remove(user)}
                          >
                            {confirming === user.id ? 'Confirm' : 'Delete'}
                          </button>

                          <button
                            type="button"
                            className="btn btn-quiet delete-dictionaries"
                            disabled={isMe}
                            onClick={() => deleteDictionaries(user)}
                          >
                            {confirmingDictionaries === user.id ? 'Confirm deletion' : 'Delete dictionaries'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}

          {data && pages > 1 && (
            <nav className="admin-pager" aria-label="Pages">
              <button
                type="button"
                className="btn btn-quiet prev"
                disabled={data.page <= 1}
                onClick={() => setPage(data.page - 1)}
              >
                Previous
              </button>
              <span className="footnote num">
                Page {formatInt(data.page)} of {formatInt(pages)}
              </span>
              <button
                type="button"
                className="btn btn-quiet next"
                disabled={data.page >= pages}
                onClick={() => setPage(data.page + 1)}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
      {tab === 'dictionaries' && <DictionaryQueue />}
      {tab === 'shelf' && <ShelfAdmin />}
    </section>
  )
}

/** The moderation queue: dictionaries their owners offered for publication. */
function DictionaryQueue() {
  const [data, setData] = useState<PendingDictionaryPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<number | null>(null)

  const reload = useCallback(
    () =>
      api
        .listPendingDictionaries({ status: 'pending' })
        .then((result) => {
          setData(result)
          setError(null)
        })
        .catch((e) => setError(e instanceof Error ? e.message : String(e))),
    [],
  )

  useEffect(() => {
    void reload()
  }, [reload])

  const decide = async (id: number, approve: boolean) => {
    setBusy(id)

    try {
      await (approve ? api.approveDictionary(id) : api.rejectDictionary(id))
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  if (error) return <p className="footnote error">{error}</p>
  if (!data) return null
  if (data.items.length === 0) return <p className="footnote">Nothing is waiting for review.</p>

  return (
    <ul className="admin-queue">
      {data.items.map((item) => (
        <li key={item.id}>
          <div className="admin-queue-head">
            <strong>{item.name}</strong>
            <span className="caption">{item.ownerName}</span>
            <span className="num">{formatInt(item.wordsCount)}</span>
          </div>
          <p className="footnote admin-queue-words">{item.topWords.join(', ')}</p>
          <div className="admin-queue-actions">
            <button
              type="button"
              className="btn btn-secondary"
              disabled={busy === item.id}
              onClick={() => void decide(item.id, true)}
            >
              Approve
            </button>
            <button
              type="button"
              className="btn btn-quiet"
              disabled={busy === item.id}
              onClick={() => void decide(item.id, false)}
            >
              Reject
            </button>
          </div>
        </li>
      ))}
    </ul>
  )
}

/**
 * Every word the signed-in admin's shelves could ever cover — the shared vocabulary plus their
 * own personal words — filterable by shelf, with a picker on each row to re-shelve it right
 * there. There is no user picker: this is always the caller's own shelves, the same as
 * `POST /api/sorting/mark` already scopes to.
 */
function ShelfAdmin() {
  const [status, setStatus] = useState<SortStatus | ''>('')
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ShelfWordPage | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(query.trim())
      setPage(1)
    }, searchDelayMs)

    return () => clearTimeout(handle)
  }, [query])

  const reload = useCallback(
    () =>
      api
        .listShelfWords({ status: status || undefined, search, page })
        .then((result) => {
          setData(result)
          setError(null)
        })
        .catch((e) => setError(e instanceof Error ? e.message : String(e))),
    [status, search, page],
  )

  useEffect(() => {
    void reload()
  }, [reload])

  const move = (word: ShelfWord, next: SortStatus) => {
    setError(null)

    api
      .mark(word.wordPairId, next)
      .then(reload)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }

  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <>
      <div className="admin-toolbar">
        <label className="field admin-search">
          <input
            type="search"
            placeholder="Search by word"
            aria-label="Search words"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </label>

        <label className="field shelf-filter-field">
          <select
            className="shelf-filter"
            aria-label="Filter by shelf"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as SortStatus | '')
              setPage(1)
            }}
          >
            <option value="">All</option>
            <option value="known">Know</option>
            <option value="unknown">Don&apos;t know</option>
            <option value="excluded">Excluded</option>
          </select>
        </label>

        {data && (
          <span className="footnote admin-count">
            {data.total === 0
              ? 'No words match'
              : `${formatInt(data.total)} ${plural(data.total, 'word', 'words')}`}
          </span>
        )}
      </div>

      {error && <p className="error">{error}</p>}
      {!data && !error && <p className="footnote">Loading…</p>}

      {data && data.items.length > 0 && (
        <table className="admin-table">
          <thead>
            <tr>
              <th scope="col">Word</th>
              <th scope="col">Translation</th>
              <th scope="col">Shelf</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((item) => (
              <tr key={item.wordPairId}>
                <th scope="row">{item.word}</th>
                <td>{item.translation}</td>
                <td className="shelf">
                  <label className="field shelf-field">
                    <select
                      className="shelf-select"
                      aria-label={`Shelf for ${item.word}`}
                      value={item.status ?? 'new'}
                      onChange={(e) => move(item, e.target.value as SortStatus)}
                    >
                      <option value="new" disabled>
                        New
                      </option>
                      <option value="known">Know</option>
                      <option value="unknown">Don&apos;t know</option>
                      <option value="excluded">Excluded</option>
                    </select>
                  </label>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {data && pages > 1 && (
        <nav className="admin-pager" aria-label="Pages">
          <button
            type="button"
            className="btn btn-quiet prev"
            disabled={data.page <= 1}
            onClick={() => setPage(data.page - 1)}
          >
            Previous
          </button>
          <span className="footnote num">
            Page {formatInt(data.page)} of {formatInt(pages)}
          </span>
          <button
            type="button"
            className="btn btn-quiet next"
            disabled={data.page >= pages}
            onClick={() => setPage(data.page + 1)}
          >
            Next
          </button>
        </nav>
      )}
    </>
  )
}
