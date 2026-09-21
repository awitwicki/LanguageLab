import { describe, expect, it } from 'vitest'
import { chaptersLabel, formatBytes, formatDue, formatInt, formatProgress, percentOf, plural, wordsLabel } from './format'

describe('formatBytes', () => {
  it('bytes under a kilobyte stay bytes', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(512)).toBe('512 B')
  })

  it('kilobytes are whole numbers', () => {
    expect(formatBytes(1536)).toBe('2 kB')
    expect(formatBytes(820 * 1024)).toBe('820 kB')
  })

  it('megabytes keep one decimal', () => {
    expect(formatBytes(1_468_006)).toBe('1.4 MB')
    expect(formatBytes(12 * 1024 * 1024)).toBe('12.0 MB')
  })
})

describe('percentOf', () => {
  it('0 of 0 is 0%, not NaN', () => {
    expect(percentOf(0, 0)).toBe(0)
  })

  it('rounds to a whole number', () => {
    expect(percentOf(1, 3)).toBe(33)
    expect(percentOf(2, 3)).toBe(67)
  })

  it('never exceeds 100 when sorted overtakes total', () => {
    expect(percentOf(5, 3)).toBe(100)
  })
})

describe('formatInt', () => {
  it('separates thousands with a space', () => {
    expect(formatInt(0)).toBe('0')
    expect(formatInt(999)).toBe('999')
    expect(formatInt(1240)).toBe('1 240')
    expect(formatInt(1234567)).toBe('1 234 567')
  })
})

describe('plural', () => {
  it.each([
    [0, 'words'],
    [1, 'word'],
    [2, 'words'],
    [11, 'words'],
    [21, 'words'],
    [1240, 'words'],
    [-1, 'word'],
  ])('%i → %s', (n, expected) => {
    expect(plural(n, 'word', 'words')).toBe(expected)
  })
})

describe('labels', () => {
  it('wordsLabel', () => {
    expect(wordsLabel(1)).toBe('1 word')
    expect(wordsLabel(1240)).toBe('1 240 words')
  })

  it('chaptersLabel', () => {
    expect(chaptersLabel(1)).toBe('1 chapter')
    expect(chaptersLabel(3)).toBe('3 chapters')
    expect(chaptersLabel(12)).toBe('12 chapters')
  })

  it('formatProgress — "X of Y"', () => {
    expect(formatProgress(500, 2000)).toBe('500 of 2 000')
  })
})

describe('formatDue', () => {
  const now = new Date('2026-09-07T12:00:00Z')

  it('learned or without a due date — "learned"', () => {
    expect(formatDue(null, false, now)).toBe('learned')
    expect(formatDue('2026-09-08T00:00:00Z', true, now)).toBe('learned')
  })

  it('today and tomorrow — in words, by UTC day', () => {
    expect(formatDue('2026-09-07T23:30:00Z', false, now)).toBe('today')
    expect(formatDue('2026-09-08T00:10:00Z', false, now)).toBe('tomorrow')
  })

  it('anything later — dd.MM', () => {
    expect(formatDue('2026-09-14T12:00:00Z', false, now)).toBe('14.09')
    expect(formatDue('2026-10-07T12:00:00Z', false, now)).toBe('07.10')
  })
})
