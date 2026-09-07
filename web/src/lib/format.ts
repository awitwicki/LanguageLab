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
  return `${formatInt(sorted)} з ${formatInt(total)}`
}

/** Українська множина: [1 слово, 2 слова, 5 слів]; 11–19 — завжди третя форма. */
export function pluralUk(n: number, forms: [string, string, string]): string {
  const abs = Math.abs(n) % 100
  const last = abs % 10

  if (abs >= 11 && abs <= 19) {
    return forms[2]
  }

  if (last === 1) {
    return forms[0]
  }

  if (last >= 2 && last <= 4) {
    return forms[1]
  }

  return forms[2]
}

export function wordsLabel(n: number): string {
  return `${formatInt(n)} ${pluralUk(n, ['слово', 'слова', 'слів'])}`
}

export function chaptersLabel(n: number): string {
  return `${formatInt(n)} ${pluralUk(n, ['глава', 'глави', 'глав'])}`
}
