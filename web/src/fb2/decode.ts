/**
 * fb2 масово ходять у windows-1251. `file.text()` завжди читає як utf-8 і
 * перетворив би половину книжки на U+FFFD — а це потім вилізло б у черзі
 * сортування як тисячі псевдослів. Тому читаємо байти й декодуємо самі.
 */
export function decodeFb2(buffer: ArrayBuffer): string {
  const head = new TextDecoder('ascii').decode(buffer.slice(0, 256))
  const declared = head.match(/encoding\s*=\s*["']([\w-]+)["']/i)?.[1]?.toLowerCase()

  if (declared) {
    try {
      return new TextDecoder(declared).decode(buffer)
    } catch {
      // Невідомий лейбл — краще прочитати як utf-8, ніж впасти.
    }
  }

  return new TextDecoder('utf-8').decode(buffer)
}
