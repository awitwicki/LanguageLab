import { describe, expect, it } from 'vitest'
import { bytesOf } from '../test/readerFixtures'
import { MemoryBookStore, type BookMeta } from './bookStore'

const meta = (hash: string): BookMeta => ({
  hash,
  title: `Book ${hash}`,
  author: '',
  fileName: `${hash}.fb2`,
  addedAt: '2026-09-24T10:00:00.000Z',
})

describe('MemoryBookStore', () => {
  it('keeps books and their bytes', async () => {
    const store = new MemoryBookStore()

    await store.put(meta('a'), bytesOf('hello'))

    expect(await store.list()).toEqual([meta('a')])
    expect(new TextDecoder().decode((await store.getFile('a'))!)).toBe('hello')
    expect(await store.getFile('b')).toBeNull()
  })

  it('forgets a removed book together with its sentence translations', async () => {
    const store = new MemoryBookStore()
    await store.put(meta('a'), bytesOf('x'))
    await store.put(meta('b'), bytesOf('y'))
    await store.putTranslation('a', '0.0.0', 'Привіт.')
    await store.putTranslation('b', '0.0.0', 'Бувай.')

    await store.remove('a')

    expect((await store.list()).map((b) => b.hash)).toEqual(['b'])
    expect(await store.getTranslation('a', '0.0.0')).toBeNull()
    expect(await store.getTranslation('b', '0.0.0')).toBe('Бувай.')
  })
})
