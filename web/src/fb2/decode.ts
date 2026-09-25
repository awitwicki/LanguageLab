/**
 * fb2 files are very often windows-1251. `file.text()` always reads as utf-8 and
 * would turn half the book into U+FFFD — which would later surface in the sorting
 * queue as thousands of pseudo-words. So read the bytes and decode ourselves.
 */
export function decodeFb2(buffer: ArrayBuffer): string {
  const head = new TextDecoder('ascii').decode(buffer.slice(0, 256))
  const declared = head.match(/encoding\s*=\s*["']([\w-]+)["']/i)?.[1]?.toLowerCase()

  if (declared) {
    try {
      return new TextDecoder(declared).decode(buffer)
    } catch {
      // Unknown label — better to read as utf-8 than to crash.
    }
  }

  return new TextDecoder('utf-8').decode(buffer)
}
