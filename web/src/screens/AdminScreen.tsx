import { useCallback, useEffect, useState } from 'react'
import { api, type AdminUser } from '../api/client'
import './AdminScreen.css'

interface Props {
  /** The signed-in admin. Their own row is read-only — the server refuses those actions anyway. */
  meId: number
}

const dateFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium' })

function formatDate(value: string | null) {
  return value ? dateFormat.format(new Date(value)) : '—'
}

export function AdminScreen({ meId }: Props) {
  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<number | null>(null)

  const reload = useCallback(
    () =>
      api
        .listUsers()
        .then((items) => {
          setUsers(items)
          setError(null)
        })
        .catch((e) => setError(e instanceof Error ? e.message : String(e))),
    [],
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

  return (
    <section className="admin">
      <h1 className="large-title">Users</h1>

      {error && <p className="error">{error}</p>}
      {!users && !error && <p className="footnote">Loading…</p>}

      {users && (
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
            {users.map((user) => {
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
    </section>
  )
}
