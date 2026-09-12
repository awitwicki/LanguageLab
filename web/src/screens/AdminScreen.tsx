import { useCallback, useEffect, useState } from 'react'
import { api, type AdminUser, type AdminUserPage } from '../api/client'
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
  // `query` is what is typed, `search` is what was last sent: the two differ during the
  // debounce so a keystroke does not fire a request.
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<AdminUserPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<number | null>(null)

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

  const pages = data ? pageCount(data) : 1

  return (
    <section className="admin">
      <h1 className="large-title">Users</h1>

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
                  <td className="role">{user.role === 'admin' ? 'Admin' : 'User'}</td>
                  <td className="status">{user.isBanned ? 'Banned' : 'Active'}</td>
                  <td className="num">{formatDate(user.createdAt)}</td>
                  <td className="num">{formatDate(user.lastLoginAt)}</td>
                  <td className="actions">
                    {user.role === 'admin' ? (
                      <button
                        type="button"
                        className="btn btn-quiet demote"
                        disabled={isMe}
                        onClick={() => void run(() => api.setUserRole(user.id, 'user'))}
                      >
                        Demote
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-quiet promote"
                        disabled={isMe}
                        onClick={() => void run(() => api.setUserRole(user.id, 'admin'))}
                      >
                        Promote
                      </button>
                    )}

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
    </section>
  )
}
