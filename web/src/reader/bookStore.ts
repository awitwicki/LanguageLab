/** What the library lists without loading a file. */
export interface BookMeta {
  hash: string
  title: string
  author: string
  fileName: string
  /** ISO 8601. */
  addedAt: string
}

/**
 * The books on this device. The file bytes never leave it; sentence translations are cached
 * here too (keyed by book and "chapter.paragraph.sentence"), since the server keeps none.
 */
export interface BookStore {
  list(): Promise<BookMeta[]>
  getFile(hash: string): Promise<ArrayBuffer | null>
  put(meta: BookMeta, bytes: ArrayBuffer): Promise<void>
  /** Removes the book, its file and its cached translations. */
  remove(hash: string): Promise<void>
  getTranslation(hash: string, key: string): Promise<string | null>
  putTranslation(hash: string, key: string, text: string): Promise<void>
}

/** For tests, and the fallback when IndexedDB is unavailable: the book lives until the tab closes. */
export class MemoryBookStore implements BookStore {
  private readonly metas = new Map<string, BookMeta>()
  private readonly files = new Map<string, ArrayBuffer>()
  private readonly translations = new Map<string, string>()

  async list() {
    return [...this.metas.values()]
  }

  async getFile(hash: string) {
    return this.files.get(hash) ?? null
  }

  async put(meta: BookMeta, bytes: ArrayBuffer) {
    this.metas.set(meta.hash, meta)
    this.files.set(meta.hash, bytes)
  }

  async remove(hash: string) {
    this.metas.delete(hash)
    this.files.delete(hash)

    for (const key of [...this.translations.keys()]) {
      if (key.startsWith(`${hash}:`)) {
        this.translations.delete(key)
      }
    }
  }

  async getTranslation(hash: string, key: string) {
    return this.translations.get(`${hash}:${key}`) ?? null
  }

  async putTranslation(hash: string, key: string, text: string) {
    this.translations.set(`${hash}:${key}`, text)
  }
}

const DB_NAME = 'languagelab-reader'
const DB_VERSION = 1

/** Rejects when the browser has no usable IndexedDB (some private modes); the caller falls back to memory. */
export function openIndexedDbBookStore(): Promise<BookStore> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') {
      reject(new Error('IndexedDB is unavailable.'))
      return
    }

    const request = indexedDB.open(DB_NAME, DB_VERSION)

    request.onupgradeneeded = () => {
      const db = request.result
      db.createObjectStore('books', { keyPath: 'hash' })
      db.createObjectStore('files')
      db.createObjectStore('translations')
    }

    request.onsuccess = () => resolve(new IndexedDbBookStore(request.result))
    request.onerror = () => reject(request.error ?? new Error('IndexedDB failed to open.'))
    request.onblocked = () => reject(new Error('IndexedDB is blocked by another tab.'))
  })
}

class IndexedDbBookStore implements BookStore {
  private readonly db: IDBDatabase

  constructor(db: IDBDatabase) {
    this.db = db
  }

  list() {
    return this.read<BookMeta[]>('books', (store) => store.getAll())
  }

  async getFile(hash: string) {
    return (await this.read<ArrayBuffer | undefined>('files', (store) => store.get(hash))) ?? null
  }

  put(meta: BookMeta, bytes: ArrayBuffer) {
    return this.write(['books', 'files'], (tx) => {
      tx.objectStore('books').put(meta)
      tx.objectStore('files').put(bytes, meta.hash)
    })
  }

  remove(hash: string) {
    return this.write(['books', 'files', 'translations'], (tx) => {
      tx.objectStore('books').delete(hash)
      tx.objectStore('files').delete(hash)
      tx.objectStore('translations').delete(IDBKeyRange.bound(`${hash}:`, `${hash}:￿`))
    })
  }

  async getTranslation(hash: string, key: string) {
    return (await this.read<string | undefined>('translations', (store) => store.get(`${hash}:${key}`))) ?? null
  }

  putTranslation(hash: string, key: string, text: string) {
    return this.write(['translations'], (tx) => {
      tx.objectStore('translations').put(text, `${hash}:${key}`)
    })
  }

  private read<T>(storeName: string, query: (store: IDBObjectStore) => IDBRequest): Promise<T> {
    return new Promise((resolve, reject) => {
      const request = query(this.db.transaction(storeName, 'readonly').objectStore(storeName))
      request.onsuccess = () => resolve(request.result as T)
      request.onerror = () => reject(request.error)
    })
  }

  private write(storeNames: string[], work: (tx: IDBTransaction) => void): Promise<void> {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeNames, 'readwrite')
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error)
      tx.onabort = () => reject(tx.error ?? new Error('The storage transaction was aborted.'))
      work(tx)
    })
  }
}
