/** Відсоток посортованого, ціле 0–100. total = 0 дає 0, а не NaN. */
export function percentOf(sorted: number, total: number): number {
  if (total <= 0) {
    return 0
  }

  return Math.min(100, Math.round((sorted / total) * 100))
}

/** 1240 → «1 240». Звичайний пробіл, а не тонкий: так простіше і в тестах, і в пошуку по сторінці. */
export function formatInt(n: number): string {
  return String(Math.trunc(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ')
}

export function formatProgress(sorted: number, total: number): string {
  return `${formatInt(sorted)} of ${formatInt(total)}`
}

/** English plural: exactly 1 takes the singular, everything else (0, 2+, negatives) the plural. */
export function plural(n: number, one: string, many: string): string {
  return Math.abs(n) === 1 ? one : many
}

export function wordsLabel(n: number): string {
  return `${formatInt(n)} ${plural(n, 'word', 'words')}`
}

export function chaptersLabel(n: number): string {
  return `${formatInt(n)} ${plural(n, 'chapter', 'chapters')}`
}

const DAY_MS = 86_400_000

/**
 * Термін наступного показу для підсумку тренування. Порівнюємо UTC-доби, а не
 * локальні: Leitner ставить DueAt у UTC, і «завтра» має означати наступну добу сервера.
 */
export function formatDue(dueAt: string | null, isLearned: boolean, now: Date): string {
  if (isLearned || dueAt === null) {
    return 'learned'
  }

  const due = new Date(dueAt)
  const dueDay = Math.floor(due.getTime() / DAY_MS)
  const today = Math.floor(now.getTime() / DAY_MS)

  if (dueDay === today) {
    return 'today'
  }

  if (dueDay === today + 1) {
    return 'tomorrow'
  }

  const dd = String(due.getUTCDate()).padStart(2, '0')
  const mm = String(due.getUTCMonth() + 1).padStart(2, '0')
  return `${dd}.${mm}`
}
