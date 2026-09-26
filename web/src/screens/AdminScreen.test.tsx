import { act } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, type AdminUser, type AdminUserPage, type PendingDictionaryPage, type ShelfWordPage } from '../api/client'
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

/** One page as the server answers it; a single page unless total says otherwise. */
function page(items: AdminUser[], extra: Partial<AdminUserPage> = {}): AdminUserPage {
  return { items, total: items.length, page: 1, pageSize: 25, ...extra }
}

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

function pageParam(path: string) {
  return Number(new URL(path, 'http://x').searchParams.get('page') ?? 1)
}

function setValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

/** Picks an option the way a user would: React hears a select through its change event. */
function choose(select: HTMLSelectElement, value: string) {
  return act(async () => {
    select.value = value
    select.dispatchEvent(new Event('change', { bubbles: true }))
  })
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('AdminScreen', () => {
  it('lists the users with their role and status', async () => {
    respond(() => ({ status: 200, body: page(users) }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const rows = container.querySelectorAll('tbody tr')

    expect(rows).toHaveLength(2)
    expect(rows[0].querySelector('.user-name')?.textContent).toBe('Ada Vance')
    expect(rows[0].querySelector<HTMLSelectElement>('.role-select')?.value).toBe('admin')
    expect(rows[1].querySelector<HTMLSelectElement>('.role-select')?.value).toBe('user')
  })

  it('offers every role in the picker, least trusted first', async () => {
    respond(() => ({ status: 200, body: page(users) }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const options = [...container.querySelectorAll('tbody tr')[1].querySelectorAll('.role-select option')]

    expect(options.map((o) => (o as HTMLOptionElement).value)).toEqual(['user', 'admin'])
    expect(options.map((o) => o.textContent)).toEqual(['User', 'Admin'])
  })

  it('changes a role through the picker and reloads the list', async () => {
    let role = 'user'

    respond((path, method) => {
      if (path === '/api/admin/users/2/role' && method === 'POST') {
        role = 'admin'
        return { status: 204 }
      }

      return {
        status: 200,
        body: page(users.map((u) => (u.id === 2 ? { ...u, role: role as AdminUser['role'] } : u))),
      }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await choose(container.querySelectorAll('tbody tr')[1].querySelector<HTMLSelectElement>('.role-select')!, 'admin')
    await flush()

    const sent = vi.mocked(fetch).mock.calls.find(([path]) => path === '/api/admin/users/2/role')

    expect(sent?.[1]?.body).toBe(JSON.stringify({ role: 'admin' }))
    expect(container.querySelectorAll('tbody tr')[1].querySelector<HTMLSelectElement>('.role-select')?.value).toBe('admin')
  })

  // The server refuses these anyway; disabling them keeps the user from discovering that
  // by being told no.
  it('disables every action on your own row', async () => {
    respond(() => ({ status: 200, body: page(users) }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const own = container.querySelectorAll('tbody tr')[0]
    const controls = [...own.querySelectorAll<HTMLButtonElement | HTMLSelectElement>('button, select')]

    expect(controls.length).toBeGreaterThan(0)
    expect(controls.every((c) => c.disabled)).toBe(true)
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
        body: page(users.map((u) => (u.id === 2 ? { ...u, isBanned: banned } : u))),
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
        : { status: 200, body: page(users) },
    )

    const { container } = await render(<AdminScreen meId={99} />)
    await flush()

    await choose(container.querySelectorAll('tbody tr')[0].querySelector<HTMLSelectElement>('.role-select')!, 'user')
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

      return { status: 200, body: page(users) }
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

  it('asks for confirmation before deleting a user\'s dictionaries', async () => {
    let sent: { path: string; method: string } | null = null

    respond((path, method) => {
      if (method === 'DELETE' && path === '/api/admin/users/2/dictionaries') {
        sent = { path, method }
        return { status: 200, body: { count: 3 } }
      }

      return { status: 200, body: page(users) }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    const row = container.querySelectorAll('tbody tr')[1]

    await click(row.querySelector('.delete-dictionaries')!)
    expect(sent).toBeNull()
    expect(row.querySelector('.delete-dictionaries')?.textContent).toBe('Confirm deletion')

    await click(row.querySelector('.delete-dictionaries')!)
    await flush()

    expect(sent).toEqual({ path: '/api/admin/users/2/dictionaries', method: 'DELETE' })
  })

  it('shows how many users match', async () => {
    respond(() => ({ status: 200, body: page(users, { total: 41 }) }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    expect(container.querySelector('.admin-count')?.textContent).toBe('41 users')
  })

  // The server filters; the screen only has to ask, and only once the admin stops typing.
  it('sends the search term after a pause in typing', async () => {
    const calls: string[] = []

    respond((path) => {
      calls.push(path)
      return { status: 200, body: page(users) }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()
    expect(calls).toEqual(['/api/admin/users?page=1'])

    vi.useFakeTimers()
    await act(async () => setValue(container.querySelector<HTMLInputElement>('input[type="search"]')!, 'bo'))
    expect(calls).toHaveLength(1)

    await act(() => vi.advanceTimersByTimeAsync(300))
    vi.useRealTimers()
    await flush()

    expect(calls.at(-1)).toBe('/api/admin/users?search=bo&page=1')
  })

  it('moves between pages', async () => {
    respond((path) => ({
      status: 200,
      body: page(pageParam(path) === 1 ? [users[0]] : [users[1]], { total: 2, page: pageParam(path), pageSize: 1 }),
    }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    expect(container.querySelector('.admin-pager')?.textContent).toContain('Page 1 of 2')
    expect(container.querySelector<HTMLButtonElement>('.prev')?.disabled).toBe(true)

    await click(container.querySelector('.next')!)
    await flush()

    expect(container.querySelector('.user-name')?.textContent).toBe('Bo Lind')
    expect(container.querySelector('.admin-pager')?.textContent).toContain('Page 2 of 2')
    expect(container.querySelector<HTMLButtonElement>('.next')?.disabled).toBe(true)
  })

  it('hides the pager when everything fits on one page', async () => {
    respond(() => ({ status: 200, body: page(users) }))

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    expect(container.querySelector('.admin-pager')).toBeNull()
  })

  // Deleting the only row of the last page must not leave the admin staring at an empty table.
  it('steps back to the last page when a delete empties the current one', async () => {
    let deleted = false

    respond((path, method) => {
      if (method === 'DELETE') {
        deleted = true
        return { status: 204 }
      }

      const remaining = deleted ? [users[0]] : users
      const current = pageParam(path)
      const items = remaining.slice(current - 1, current)

      return { status: 200, body: page(items, { total: remaining.length, page: current, pageSize: 1 }) }
    })

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await click(container.querySelector('.next')!)
    await flush()
    expect(container.querySelector('.user-name')?.textContent).toBe('Bo Lind')

    await click(container.querySelector('.delete')!)
    await click(container.querySelector('.delete')!)
    await flush()
    await flush()

    expect(container.querySelector('.user-name')?.textContent).toBe('Ada Vance')
    expect(container.querySelector('.admin-pager')).toBeNull()
  })
})

describe('AdminScreen — dictionaries tab', () => {
  const queue: PendingDictionaryPage = {
    items: [
      {
        id: 7,
        name: 'Wool',
        ownerId: 3,
        ownerName: 'Juliette N',
        wordsCount: 4210,
        status: 'pending',
        topWords: ['silo', 'cleaning', 'abide'],
      },
    ],
    total: 1,
    page: 1,
    pageSize: 25,
  }

  it('lists the dictionaries waiting for review', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listPendingDictionaries').mockResolvedValue(queue)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Dictionaries')!)
    await flush()

    expect(list).toHaveBeenCalled()
    expect(container.textContent).toContain('Wool')
    expect(container.textContent).toContain('Juliette N')
    expect(container.textContent).toContain('silo')
  })

  it('approves a dictionary and reloads the queue', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listPendingDictionaries').mockResolvedValue(queue)
    const approve = vi.spyOn(api, 'approveDictionary').mockResolvedValue(null)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Dictionaries')!)
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Approve')!)
    await flush()

    expect(approve).toHaveBeenCalledWith(7)
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('rejects a dictionary', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    vi.spyOn(api, 'listPendingDictionaries').mockResolvedValue(queue)
    const reject = vi.spyOn(api, 'rejectDictionary').mockResolvedValue(null)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Dictionaries')!)
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Reject')!)
    await flush()

    expect(reject).toHaveBeenCalledWith(7)
  })
})

describe('AdminScreen — shelf tab', () => {
  const shelfPage: ShelfWordPage = {
    items: [
      { wordPairId: 1, word: 'silo', translation: 'бункер', status: 'known' },
      { wordPairId: 2, word: 'abide', translation: 'миритися', status: null },
    ],
    total: 2,
    page: 1,
    pageSize: 25,
  }

  async function openShelfTab(container: HTMLElement) {
    await click(buttons(container).find((b) => b.textContent === 'Shelf')!)
    await flush()
  }

  it('lists the shelf words with their current shelf', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listShelfWords').mockResolvedValue(shelfPage)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()
    await openShelfTab(container)

    expect(list).toHaveBeenCalled()
    const rows = container.querySelectorAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].querySelector('th')?.textContent).toBe('silo')
    expect(rows[0].querySelector<HTMLSelectElement>('.shelf-select')?.value).toBe('known')
    expect(rows[1].querySelector<HTMLSelectElement>('.shelf-select')?.value).toBe('new')
  })

  it('filters by shelf through the status select', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listShelfWords').mockResolvedValue(shelfPage)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()
    await openShelfTab(container)

    await choose(container.querySelector<HTMLSelectElement>('.shelf-filter')!, 'unknown')
    await flush()

    expect(list).toHaveBeenLastCalledWith({ status: 'unknown', search: '', page: 1 })
  })

  it('moves a word to a different shelf and reloads', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listShelfWords').mockResolvedValue(shelfPage)
    const mark = vi.spyOn(api, 'mark').mockResolvedValue(null)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()
    await openShelfTab(container)

    const row = container.querySelectorAll('tbody tr')[1]
    await choose(row.querySelector<HTMLSelectElement>('.shelf-select')!, 'unknown')
    await flush()

    expect(mark).toHaveBeenCalledWith(2, 'unknown')
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('sends the search term after a pause in typing', async () => {
    respond(() => ({ status: 200, body: page(users) }))
    const list = vi.spyOn(api, 'listShelfWords').mockResolvedValue(shelfPage)

    const { container } = await render(<AdminScreen meId={1} />)
    await flush()
    await openShelfTab(container)
    list.mockClear()

    vi.useFakeTimers()
    await act(async () => setValue(container.querySelector<HTMLInputElement>('input[type="search"]')!, 'silo'))
    expect(list).not.toHaveBeenCalled()

    await act(() => vi.advanceTimersByTimeAsync(300))
    vi.useRealTimers()
    await flush()

    expect(list).toHaveBeenLastCalledWith({ status: undefined, search: 'silo', page: 1 })
  })
})
