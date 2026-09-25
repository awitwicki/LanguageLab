/**
 * fb2 files are very often windows-1251, and an epub's XHTML documents may declare an encoding of
 * their own. `file.text()` always reads as utf-8 and would turn half the book into U+FFFD — which
 * would later surface in the sorting queue as thousands of pseudo-words. So read the bytes and
 * decode ourselves.
 */
export function decodeXml(bytes: ArrayBuffer | Uint8Array): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes)
  const head = new TextDecoder('ascii').decode(view.subarray(0, 256))
  const declared = head.match(/(?:encoding|charset)\s*=\s*["']?([\w-]+)/i)?.[1]?.toLowerCase()

  if (declared) {
    try {
      return new TextDecoder(declared).decode(view)
    } catch {
      // Unknown label — better to read as utf-8 than to crash.
    }
  }

  return new TextDecoder('utf-8').decode(view)
}
