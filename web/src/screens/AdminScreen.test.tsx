import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AdminUser } from '../api/client'
import { click, flush, render } from '../test/render'
import { AdminScreen } from './AdminScreen'

const users: AdminUser[] = [
  {
    id: 1, telegramUserId: 101, displayName: 'Ada Vance', username: 'ada', photoUrl: null,
    role: 'admin', isBanned: false, createdAt: '2026-09-01T00:00:00Z', lastLoginAt: '2026-09-08T00:00:00Z',
  },
  {
    id: 2, telegramUserId: 102, displayName: 'Bo Lind', username: null, photoUrl: null,
    role: 'user', isBanned: false, createdAt: '2026-09-02T00:00:00Z', lastLoginAt: null,
  },
]

function respond(handler: (path: string, method: string) => { status: number; body?: unknown }) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string, init?: RequestInit) => {
      const route = handler(path, init?.method ?? 'GET')

      return Promise.resolve({
        ok: route.status >= 200 && route.status < 300,
        status: route.status,
        json: () => Promise.resolve(route.body),
      } as Response)
    }),
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('AdminScreen', () => {
  it('lists the users with their role and status', async () => {
    respond(() => ({ status: 200, body: users }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const rows = container.querySelectorAll('tbody tr')

    expect(rows).toHaveLength(2)
    expect(rows[0].querySelector('.user-name')?.textContent).toBe('Ada Vance')
    expect(rows[0].querySelector('.role')?.textContent).toBe('Admin')
    expect(rows[1].querySelector('.role')?.textContent).toBe('User')
  })

  // The server refuses these anyway; disabling them keeps the user from discovering that
  // by being told no.
  it('disables every action on your own row', async () => {
    respond(() => ({ status: 200, body: users }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const own = container.querySelectorAll('tbody tr')[0]

    expect([...own.querySelectorAll('button')].every((b) => b.disabled)).toBe(true)
  })

  it('bans a user and reloads the list', async () => {
    let banned = false

    respond((path, method) => {
      if (path === '/api/admin/users/2/ban' && method === 'POST') {
        banned = true
        return { status: 204 }
      }

      return {
        status: 200,
        body: users.map((u) => (u.id === 2 ? { ...u, isBanned: banned } : u)),
      }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await click(container.querySelectorAll('tbody tr')[1].querySelector('.ban')!)
    await flush()

    expect(banned).toBe(true)
    expect(container.querySelectorAll('tbody tr')[1].querySelector('.status')?.textContent).toBe('Banned')
  })

  it('shows the reason when the server refuses', async () => {
    respond((_path, method) =>
      method === 'POST'
        ? { status: 409, body: { message: 'This is the last administrator — promote someone else first.' } }
        : { status: 200, body: users },
    )

    const { container } = await render(<AdminScreen meId={99} />)
    await flush()

    await click(container.querySelectorAll('tbody tr')[0].querySelector('.demote')!)
    await flush()

    expect(container.querySelector('.error')?.textContent).toBe(
      'This is the last administrator — promote someone else first.',
    )
  })

  it('asks for confirmation before deleting', async () => {
    let deleted = false

    respond((_path, method) => {
      if (method === 'DELETE') {
        deleted = true
        return { status: 204 }
      }

      return { status: 200, body: users }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const row = container.querySelectorAll('tbody tr')[1]

    await click(row.querySelector('.delete')!)
    expect(deleted).toBe(false)
    expect(row.querySelector('.delete')?.textContent).toBe('Confirm')

    await click(row.querySelector('.delete')!)
    await flush()

    expect(deleted).toBe(true)
  })
})
