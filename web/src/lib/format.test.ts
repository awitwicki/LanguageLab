import { describe, expect, it } from 'vitest'
import { chaptersLabel, formatInt, formatProgress, percentOf, pluralUk, wordsLabel } from './format'

describe('percentOf', () => {
  it('0 з 0 — це 0%, а не NaN', () => {
    expect(percentOf(0, 0)).toBe(0)
  })

  it('округлює до цілого', () => {
    expect(percentOf(1, 3)).toBe(33)
    expect(percentOf(2, 3)).toBe(67)
  })

  it('не перевищує 100, якщо sorted обігнав total', () => {
    expect(percentOf(5, 3)).toBe(100)
  })
})

describe('formatInt', () => {
  it('розділяє тисячі пробілом', () => {
    expect(formatInt(0)).toBe('0')
    expect(formatInt(999)).toBe('999')
    expect(formatInt(1240)).toBe('1 240')
    expect(formatInt(1234567)).toBe('1 234 567')
  })
})

describe('pluralUk', () => {
  const forms: [string, string, string] = ['слово', 'слова', 'слів']

  it.each([
    [0, 'слів'],
    [1, 'слово'],
    [2, 'слова'],
    [4, 'слова'],
    [5, 'слів'],
    [11, 'слів'],
    [12, 'слів'],
    [21, 'слово'],
    [22, 'слова'],
    [100, 'слів'],
    [1240, 'слів'],
    [1241, 'слово'],
  ])('%i → %s', (n, expected) => {
    expect(pluralUk(n, forms)).toBe(expected)
  })
})

describe('labels', () => {
  it('wordsLabel', () => {
    expect(wordsLabel(1)).toBe('1 слово')
    expect(wordsLabel(1240)).toBe('1 240 слів')
  })

  it('chaptersLabel', () => {
    expect(chaptersLabel(1)).toBe('1 глава')
    expect(chaptersLabel(3)).toBe('3 глави')
    expect(chaptersLabel(12)).toBe('12 глав')
  })

  it('formatProgress — «X з Y»', () => {
    expect(formatProgress(500, 2000)).toBe('500 з 2 000')
  })
})
