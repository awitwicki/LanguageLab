export interface DictionaryListItem {
  id: number
  name: string
  wordsCount: number
  hasChapters: boolean
  sortedCount: number
}

export interface ChapterView {
  id: number
  order: number
  title: string
  wordsCount: number
  sortedCount: number
}

export interface DictionaryDetail {
  id: number
  name: string
  wordsCount: number
  sortedCount: number
  chapters: ChapterView[]
}

export interface ImportWord {
  word: string
  count: number
}

export interface ImportChapter {
  order: number
  title: string
  words: ImportWord[]
}

export interface ImportResult {
  dictionaryId: number
  totalWords: number
  newWords: number
  reusedWords: number
}

export interface QueueWord {
  wordPairId: number
  word: string
  frequency: number
}

export interface SortingQueue {
  words: QueueWord[]
  total: number
  sorted: number
  remaining: number
}

export type SortStatus = 'known' | 'unknown' | 'excluded'

export interface UndoResult {
  wordPairId: number
  word: string
  previousStatus: SortStatus
}

export interface RecentWord {
  wordPairId: number
  word: string
}

export interface RecentWords {
  known: RecentWord[]
  unknown: RecentWord[]
}

async function request<T>(path: string, init?: RequestInit): Promise<T | null> {
  const response = await fetch(path, {
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
  })

  if (!response.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} → ${response.status}`)
  }

  if (response.status === 204) {
    return null
  }

  return (await response.json()) as T
}

export const api = {
  listDictionaries: () => request<DictionaryListItem[]>('/api/dictionaries') as Promise<DictionaryListItem[]>,

  getDictionary: (id: number) =>
    request<DictionaryDetail>(`/api/dictionaries/${id}`) as Promise<DictionaryDetail>,

  deleteDictionary: (id: number) => request<null>(`/api/dictionaries/${id}`, { method: 'DELETE' }),

  importDictionary: (payload: { name: string; chapters?: ImportChapter[]; words?: ImportWord[] }) =>
    request<ImportResult>('/api/dictionaries/import', {
      method: 'POST',
      body: JSON.stringify(payload),
    }) as Promise<ImportResult>,

  getQueue: (dictionaryId: number, chapterIds: number[] | null, take = 50) => {
    const params = new URLSearchParams({ dictionaryId: String(dictionaryId), take: String(take) })

    if (chapterIds && chapterIds.length > 0) {
      params.set('chapterIds', chapterIds.join(','))
    }

    return request<SortingQueue>(`/api/sorting/queue?${params}`) as Promise<SortingQueue>
  },

  mark: (wordPairId: number, status: SortStatus) =>
    request<null>('/api/sorting/mark', {
      method: 'POST',
      body: JSON.stringify({ wordPairId, status }),
    }),

  undo: () => request<UndoResult>('/api/sorting/undo', { method: 'POST' }),

  getRecent: (take = 10) =>
    request<RecentWords>(`/api/sorting/recent?take=${take}`) as Promise<RecentWords>,
}
