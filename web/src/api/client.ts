export type UserRole = 'user' | 'admin'

export interface CurrentUser {
  id: number
  telegramUserId: number
  displayName: string
  username: string | null
  photoUrl: string | null
  role: UserRole
}

export interface AdminUser extends CurrentUser {
  isBanned: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface DictionaryListItem {
  id: number
  name: string
  wordsCount: number
  hasChapters: boolean
  sortedCount: number
}

/** Розклад слів скоупу по боксах Leitner. boxes: індекс 0 = бокс 1, лише не вивчені; вивчені — learned. */
export interface LearningProgress {
  notStarted: number
  boxes: number[]
  learned: number
  total: number
}

export interface ChapterView {
  id: number
  order: number
  title: string
  wordsCount: number
  sortedCount: number
  learnableCount: number
  learning: LearningProgress
}

export interface TopWord {
  wordPairId: number
  word: string
  frequency: number
}

export interface DictionaryDetail {
  id: number
  name: string
  wordsCount: number
  sortedCount: number
  learnableCount: number
  dueCount: number
  learning: LearningProgress
  chapters: ChapterView[]
  topWords: TopWord[]
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

export type TrainingMode = 'newBatch' | 'review'

export type QuestionDirection = 'enToUa' | 'uaToEn'

export interface BatchWord {
  wordPairId: number
  word: string
  translation: string
}

export interface TrainingStarted {
  trainingId: number
  mode: TrainingMode
  words: BatchWord[]
  totalQuestions: number
}

export interface BatchCandidate {
  wordPairId: number
  word: string
  translation: string
  /** Частота в тому скоупі, який тренуємо: по главі або по книжці. */
  frequency: number
}

export interface BatchPreview {
  learning: LearningProgress
  learnableCount: number
  candidates: BatchCandidate[]
}

export interface QuestionOption {
  wordPairId: number
  label: string
}

export interface QuestionDto {
  id: number
  wordPairId: number
  direction: QuestionDirection
  prompt: string
  options: QuestionOption[]
}

export interface NextQuestion {
  question: QuestionDto | null
  answered: number
  total: number
}

export interface AnswerResult {
  isCorrect: boolean
  correctWordPairId: number
  word: string
  translation: string
}

export interface WordResult {
  word: string
  translation: string
  correct: number
  total: number
  box: number
  dueAt: string | null
  isLearned: boolean
}

export interface TrainingSummary {
  correct: number
  total: number
  ratio: number
  passed: boolean
  words: WordResult[]
}

let unauthorizedHandler: () => void = () => {}

/**
 * Called when any request finds the session gone — banned, signed out elsewhere, expired.
 * `getMe` deliberately does not go through `request`, so the signed-out probe at boot never
 * fires this.
 */
export function setUnauthorizedHandler(handler: () => void) {
  unauthorizedHandler = handler
}

// Guarded actions answer 409 with { message }: the reason is written for the user, so show
// it instead of the status code.
async function errorMessage(response: Response, method: string, path: string) {
  try {
    const body = (await response.json()) as { message?: string }

    if (body?.message) {
      return body.message
    }
  } catch {
    // Not a JSON body — fall through to the generic message.
  }

  return `${method} ${path} → ${response.status}`
}

async function request<T>(path: string, init?: RequestInit): Promise<T | null> {
  const response = await fetch(path, {
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
  })

  if (!response.ok) {
    if (response.status === 401) {
      unauthorizedHandler()
    }

    throw new Error(await errorMessage(response, init?.method ?? 'GET', path))
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

  importDictionary: (payload: {
    name: string
    chapters?: ImportChapter[]
    words?: ImportWord[]
    isPublic?: boolean
  }) =>
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

  // 204 (нема слів / нема прострочених / нема помилок) приходить як null — це стан, не помилка.
  // wordPairIds — «що бачив у превью, те й тренуєш»; без них сервер бере той самий топ за частотою.
  startNewBatch: (dictionaryId: number, chapterIds: number[] | null, batchSize: number, wordPairIds: number[] | null = null) =>
    request<TrainingStarted>('/api/training/new-batch', {
      method: 'POST',
      body: JSON.stringify({
        dictionaryId,
        chapterIds: chapterIds && chapterIds.length > 0 ? chapterIds : null,
        batchSize,
        wordPairIds: wordPairIds && wordPairIds.length > 0 ? wordPairIds : null,
      }),
    }),

  previewBatch: (dictionaryId: number, chapterIds: number[] | null, take: number) => {
    const params = new URLSearchParams({ dictionaryId: String(dictionaryId), take: String(take) })

    if (chapterIds && chapterIds.length > 0) {
      params.set('chapterIds', chapterIds.join(','))
    }

    return request<BatchPreview>(`/api/training/preview?${params}`) as Promise<BatchPreview>
  },

  startReview: () => request<TrainingStarted>('/api/training/review', { method: 'POST' }),

  retry: (trainingId: number) =>
    request<TrainingStarted>(`/api/training/${trainingId}/retry`, { method: 'POST' }),

  nextQuestion: (trainingId: number) =>
    request<NextQuestion>(`/api/training/${trainingId}/next`) as Promise<NextQuestion>,

  answer: (trainingId: number, questionId: number, pickedWordPairId: number) =>
    request<AnswerResult>(`/api/training/${trainingId}/answer`, {
      method: 'POST',
      body: JSON.stringify({ questionId, pickedWordPairId }),
    }),

  markKnown: (trainingId: number, questionId: number) =>
    request<{ word: string }>(`/api/training/${trainingId}/known`, {
      method: 'POST',
      body: JSON.stringify({ questionId }),
    }),

  finish: (trainingId: number) =>
    request<TrainingSummary>(`/api/training/${trainingId}/finish`, { method: 'POST' }) as Promise<TrainingSummary>,

  // Raw fetch, not request(): 401 here means "not signed in yet", which is an answer, not a
  // dropped session.
  getMe: async (): Promise<CurrentUser | null> => {
    const response = await fetch('/api/auth/me')

    if (response.status === 401) {
      return null
    }

    if (!response.ok) {
      throw new Error(`GET /api/auth/me → ${response.status}`)
    }

    return (await response.json()) as CurrentUser
  },

  logout: () => request<null>('/api/auth/logout', { method: 'POST' }),

  listUsers: () => request<AdminUser[]>('/api/admin/users') as Promise<AdminUser[]>,

  banUser: (id: number) => request<null>(`/api/admin/users/${id}/ban`, { method: 'POST' }),

  unbanUser: (id: number) => request<null>(`/api/admin/users/${id}/unban`, { method: 'POST' }),

  setUserRole: (id: number, role: UserRole) =>
    request<null>(`/api/admin/users/${id}/role`, {
      method: 'POST',
      body: JSON.stringify({ role }),
    }),

  deleteUser: (id: number) => request<null>(`/api/admin/users/${id}`, { method: 'DELETE' }),

  setDictionaryVisibility: (id: number, isPublic: boolean) =>
    request<null>(`/api/dictionaries/${id}`, {
      method: 'PATCH',
      body: JSON.stringify({ isPublic }),
    }),
}
