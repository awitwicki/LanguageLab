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
    await store.putTranslation('a', 'uk', '0.0.0', 'Привіт.')
    await store.putTranslation('b', 'uk', '0.0.0', 'Бувай.')

    await store.remove('a')

    expect((await store.list()).map((b) => b.hash)).toEqual(['b'])
    expect(await store.getTranslation('a', 'uk', '0.0.0')).toBeNull()
    expect(await store.getTranslation('b', 'uk', '0.0.0')).toBe('Бувай.')
  })

  it('keeps translations of the same sentence in different languages apart', async () => {
    const store = new MemoryBookStore()

    await store.putTranslation('a', 'uk', '0.0.0', 'Привіт.')
    await store.putTranslation('a', 'pl', '0.0.0', 'Cześć.')

    expect(await store.getTranslation('a', 'uk', '0.0.0')).toBe('Привіт.')
    expect(await store.getTranslation('a', 'pl', '0.0.0')).toBe('Cześć.')
  })
})
