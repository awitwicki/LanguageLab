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
  /** Words in progress whose Leitner due date has passed — what a chapter "Review" would take. */
  dueCount: number
  /** When the next in-progress word comes due, or null when none is waiting. ISO 8601. */
  nextDueAt: string | null
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

export type VerbLearnState = 'new' | 'learning1' | 'learning2' | 'learning3' | 'learned' | 'mastered' | 'forgotten'

export type FamilyStatus = 'locked' | 'available' | 'done'

export type SessionMode = 'learn' | 'errorsOnly' | 'mixed'

export type ExerciseType = 'card' | 'gapChoice' | 'oddOne' | 'match' | 'formPick' | 'gapType' | 'tripleType'

export type FormAsked = 'v2' | 'v3' | 'both' | 'recognition'

export type Tense = 'present' | 'past' | 'perfect'

export type ErrorKind = 'edSuffix' | 'v2ForV3' | 'v3ForV2' | 'wrongFamily' | 'spelling' | 'other'

export type AttemptOutcome = 'correct' | 'wrong' | 'neutral'

export interface VerbRowView {
  v1: string
  v2: string
  v3: string
  translation: string
  state: VerbLearnState
  flagged: boolean
}

export interface FamilyView {
  key: string
  title: string
  status: FamilyStatus
  total: number
  learned: number
  verbs: VerbRowView[]
}

export interface GroupView {
  group: number
  title: string
  total: number
  learned: number
  unlocked: boolean
  families: FamilyView[]
}

export interface ActiveSessionView {
  id: number
  mode: SessionMode
  group: number | null
  family: string | null
  answered: number
  total: number
}

export interface VerbsProgress {
  learnedPercent: number
  groups: GroupView[]
  mixedAvailable: boolean
  errorsAvailable: boolean
  activeSession: ActiveSessionView | null
}

export interface SessionStarted {
  id: number
  mode: SessionMode
  group: number | null
  family: string | null
  title: string
  total: number
}

export interface VerbDto {
  v1: string
  translation: string
  group: number
  family: string
  suffixes: string[]
}

export interface ExampleDto {
  tense: Tense
  text: string
}

export interface CardBlock {
  v2: string[]
  v3: string[]
  examples: ExampleDto[]
  note: string | null
}

export interface GapChoiceBlock {
  sentence: string
  tense: Tense
  options: string[]
}

export interface OddOneBlock {
  options: string[]
}

export interface MatchPair {
  left: string
  right: string
}

export interface MatchBlock {
  form: FormAsked
  lefts: string[]
  rights: string[]
  matched: MatchPair[]
}

export interface FormPickBlock {
  sentence: string
}

export interface GapTypeBlock {
  sentence: string
  tense: Tense
  hint: string
}

export interface TripleTypeBlock {
  v1: string
  autofillV3: boolean
}

export interface TaskDto {
  id: number
  type: ExerciseType
  formAsked: FormAsked
  level: number
  isReturn: boolean
  verb: VerbDto
  card: CardBlock | null
  gapChoice: GapChoiceBlock | null
  oddOne: OddOneBlock | null
  match: MatchBlock | null
  formPick: FormPickBlock | null
  gapType: GapTypeBlock | null
  tripleType: TripleTypeBlock | null
}

export interface NextTask {
  task: TaskDto | null
  answered: number
  total: number
}

export interface AnswerFeedback {
  outcome: AttemptOutcome
  taskComplete: boolean
  correctAnswer: string
  triplet: string
  explanation: string
  errorKind: ErrorKind | null
  willReturn: boolean
  matched: MatchPair[] | null
}

export interface MistakeView {
  v1: string
  v2: string
  v3: string
  translation: string
  wrongCount: number
}

export interface SessionSummary {
  total: number
  correct: number
  mistakes: MistakeView[]
  learned: string[]
}

export interface ForgotResult {
  v1: string
  state: VerbLearnState
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

  /** Without a scope reviews everything due across all books; with one, only that book or those chapters. */
  startReview: (scope?: { dictionaryId: number; chapterIds: number[] | null }) =>
    request<TrainingStarted>('/api/training/review', {
      method: 'POST',
      body: scope ? JSON.stringify(scope) : undefined,
    }),

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

  getVerbsProgress: () => request<VerbsProgress>('/api/irregular-verbs/progress') as Promise<VerbsProgress>,

  /** 409 (a locked family, or nothing to train) throws with the server's message. */
  startVerbSession: (body: { mode: SessionMode; family?: string; fromSessionId?: number }) =>
    request<SessionStarted>('/api/irregular-verbs/sessions', {
      method: 'POST',
      body: JSON.stringify(body),
    }) as Promise<SessionStarted>,

  nextVerbTask: (sessionId: number) =>
    request<NextTask>(`/api/irregular-verbs/sessions/${sessionId}/next`) as Promise<NextTask>,

  // 204 (the task was already answered, e.g. a double click) comes back as null — a race, not an error.
  answerVerbTask: (sessionId: number, taskId: number, answer: string, responseMs?: number) =>
    request<AnswerFeedback>(`/api/irregular-verbs/sessions/${sessionId}/answer`, {
      method: 'POST',
      body: JSON.stringify({ taskId, answer, responseMs }),
    }),

  finishVerbSession: (sessionId: number) =>
    request<SessionSummary>(`/api/irregular-verbs/sessions/${sessionId}/finish`, { method: 'POST' }) as Promise<SessionSummary>,

  forgotVerb: (v1: string) =>
    request<ForgotResult>(`/api/irregular-verbs/verbs/${v1}/forgot`, { method: 'POST' }) as Promise<ForgotResult>,

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

  /** Answers 409 { message } when refused — the last-admin rule. */
  deleteMe: () => request<null>('/api/auth/me', { method: 'DELETE' }),

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
