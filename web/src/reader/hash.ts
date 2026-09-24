/** Lowercase hex SHA-256 of a file's bytes — a book's identity across devices (ReaderBook.FileHash on the server). */
export async function sha256Hex(buffer: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', buffer)

  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('')
}
