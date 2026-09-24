export type UserRole = 'user' | 'uploader' | 'admin'

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

/** One page of the admin list; total counts the whole filtered list, page is 1-based. */
export interface AdminUserPage {
  items: AdminUser[]
  total: number
  page: number
  pageSize: number
}

/** What a Mini App sign-in came back with. 'failed' covers a stale or bad launch and any server error. */
export type WebAppLogin = { status: 'signed-in'; user: CurrentUser } | { status: 'banned' } | { status: 'failed' }

export interface DictionaryListItem {
  id: number
  name: string
  wordsCount: number
  hasChapters: boolean
  sortedCount: number
  /** The user's own "My words" list — pinned in the sidebar, never among the books. */
  isPersonal: boolean
}

/** How a scope's words are spread across the Leitner boxes. boxes: index 0 = box 1, unlearned only; learned ones are in learned. */
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
  /** The caller's own star — listed on the home screen and at the top of the book page. */
  isStarred: boolean
}

/** One row of the home screen's starred list: the chapter plus the book it belongs to. */
export interface StarredChapter {
  dictionaryId: number
  dictionaryName: string
  chapter: ChapterView
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
  isPublic: boolean
}

export type TranslationSource = 'dictionary' | 'myMemory' | 'none'

export interface TranslationLookup {
  word: string
  translation: string | null
  source: TranslationSource
}

export type ReaderWordStatus = 'new' | 'learning' | 'known'

/** The server's record of a book read in the reader — the file itself stays on the device. */
export interface ReaderBookDto {
  fileHash: string
  title: string
  author: string
  chaptersCount: number
  chapterIndex: number
  paragraphIndex: number
  sentenceIndex: number
  /** 0..1 over the whole book. */
  progress: number
  /** ISO 8601. */
  updatedAt: string
  /** A visible dictionary imported from the same file. */
  dictionaryId: number | null
}

export interface ReaderCapabilities {
  sentenceTranslation: boolean
}

export interface WordStatusesDto {
  learning: string[]
  known: string[]
}

/** Where "Add to training" sends a word: this book's dictionary, or "My words". */
export type LearnTarget = 'book' | 'personal'

export interface ReaderWord {
  lemma: string
  translation: string | null
  source: TranslationSource
  status: ReaderWordStatus
  learnTarget: LearnTarget
}

export type SentenceTranslationResult =
  | { status: 'ok'; translation: string }
  | { status: 'limit' }
  | { status: 'quota' }
  | { status: 'tooLong' }
  | { status: 'failed' }

/** box is null until the word's first exercise; 1..5 while learning; isLearned once graduated. */
export interface PersonalWord {
  wordPairId: number
  word: string
  translation: string
  box: number | null
  isLearned: boolean
}

export interface PersonalDictionary {
  id: number
  name: string
  wordsCount: number
  learnableCount: number
  dueCount: number
  learning: LearningProgress
  words: PersonalWord[]
}

export interface BulkWordEntry {
  word: string
  translation: string
}

/** word/translation echo the normalized input, for rendering a per-line result list. */
export interface BulkWordOutcome {
  word: string
  translation: string
  added: boolean
  error: string | null
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
  translation: string
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
  translation: string
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
  /** Frequency in the scope being trained: per chapter or per book. */
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

/** The user's Leitner standing across every book. boxCounts: index 0 = box 1, words in progress only. */
export interface TrainingStats {
  boxCounts: number[]
  learned: number
  /** Sorting shelves, global across books: "know" / "don't know" / excluded. */
  known: number
  unknown: number
  excluded: number
  due: number
  correct: number
  wrong: number
}

export type VerbLearnState = 'new' | 'learning1' | 'learning2' | 'learning3' | 'learned' | 'mastered' | 'forgotten'

export type FamilyStatus = 'available' | 'done'

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

export type PronunciationState = 'new' | 'learning' | 'mastered'
export type PronunciationFamilyStatus = 'available' | 'done'
export type PronunciationOutcome = 'correct' | 'wrong'
export type Accent = 'us' | 'uk'

export interface PronunciationWordDto {
  word: string
  ipa: string
  audioUs: string
  audioUk: string
  state: PronunciationState
  streak: number
}

export interface PronunciationFamilyOverview {
  key: string
  title: string
  targetSounds: string[]
  total: number
  mastered: number
  status: PronunciationFamilyStatus
}

export interface PronunciationProgress {
  families: PronunciationFamilyOverview[]
}

export interface PronunciationFamily {
  key: string
  title: string
  targetSounds: string[]
  words: PronunciationWordDto[]
}

export interface PronunciationNextWord {
  word: PronunciationWordDto | null
}

export interface PronunciationAttemptResult {
  outcome: PronunciationOutcome
  score: number
  state: PronunciationState
  streak: number
  familyDone: boolean
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

export type UploadProgress = (sent: number, total: number) => void

/**
 * A POST with upload progress. fetch cannot report bytes sent, and a book is a megabyte or
 * two of JSON that a phone on mobile data pushes slowly enough for a bare "Uploading…" to
 * look stuck — so this one request goes over XMLHttpRequest. Failures are named: the
 * server's own message when it sends one, a size hint for a 413 (the proxy in front of the
 * API refuses big bodies, not the API), and a dropped connection instead of silence.
 */
function uploadJson<T>(path: string, payload: unknown, onProgress?: UploadProgress): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()

    xhr.open('POST', path)
    xhr.setRequestHeader('Content-Type', 'application/json')

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.(event.loaded, event.total)
      }
    }

    xhr.onerror = () =>
      reject(new Error('The connection dropped while uploading. Check the network and try again.'))

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          resolve(JSON.parse(xhr.responseText) as T)
        } catch {
          reject(new Error(`POST ${path} → ${xhr.status}, unreadable response`))
        }

        return
      }

      if (xhr.status === 401) {
        unauthorizedHandler()
      }

      reject(new Error(uploadErrorMessage(xhr.status, xhr.responseText, path)))
    }

    xhr.send(JSON.stringify(payload))
  })
}

function uploadErrorMessage(status: number, body: string, path: string): string {
  try {
    const parsed = JSON.parse(body) as { message?: string }

    if (parsed?.message) {
      return parsed.message
    }
  } catch {
    // Not a JSON body — a proxy error page, most likely.
  }

  if (status === 413) {
    return 'The server refused the upload as too large (HTTP 413). Try a book with fewer chapters, or a lower chapter level.'
  }

  return `POST ${path} → ${status}`
}

export const api = {
  listDictionaries: () => request<DictionaryListItem[]>('/api/dictionaries') as Promise<DictionaryListItem[]>,

  getDictionary: (id: number) =>
    request<DictionaryDetail>(`/api/dictionaries/${id}`) as Promise<DictionaryDetail>,

  deleteDictionary: (id: number) => request<null>(`/api/dictionaries/${id}`, { method: 'DELETE' }),

  translate: (word: string) =>
    request<TranslationLookup>(`/api/translate?${new URLSearchParams({ word })}`) as Promise<TranslationLookup>,

  readerCapabilities: () => request<ReaderCapabilities>('/api/reader/capabilities') as Promise<ReaderCapabilities>,

  listReaderBooks: () => request<ReaderBookDto[]>('/api/reader/books') as Promise<ReaderBookDto[]>,

  registerReaderBook: (hash: string, book: { title: string; author: string; chaptersCount: number }) =>
    request<ReaderBookDto>(`/api/reader/books/${hash}`, {
      method: 'PUT',
      body: JSON.stringify(book),
    }) as Promise<ReaderBookDto>,

  saveReaderPosition: (
    hash: string,
    position: {
      chapterIndex: number
      paragraphIndex: number
      sentenceIndex: number
      progress: number
      clientUpdatedAt: string
    },
  ) => request<null>(`/api/reader/books/${hash}/position`, { method: 'PUT', body: JSON.stringify(position) }),

  removeReaderBook: (hash: string) => request<null>(`/api/reader/books/${hash}`, { method: 'DELETE' }),

  getWordStatuses: () => request<WordStatusesDto>('/api/reader/word-statuses') as Promise<WordStatusesDto>,

  /** dictionaryId: the dictionary of the book being read — it decides where Add to training goes. */
  getReaderWord: (lemma: string, dictionaryId: number | null = null) =>
    request<ReaderWord>(
      `/api/reader/words/${encodeURIComponent(lemma)}${
        dictionaryId === null ? '' : `?${new URLSearchParams({ dictionaryId: String(dictionaryId) })}`
      }`,
    ) as Promise<ReaderWord>,

  // 400 carries { message } ("Type a translation first.").
  learnWord: (lemma: string, translation?: string, dictionaryId: number | null = null) =>
    request<null>(`/api/reader/words/${encodeURIComponent(lemma)}/learn`, {
      method: 'POST',
      body: JSON.stringify({ translation: translation ?? null, dictionaryId }),
    }),

  knowWord: (lemma: string) =>
    request<null>(`/api/reader/words/${encodeURIComponent(lemma)}/known`, { method: 'POST' }),

  /** The "exclude" shelf: a name or another non-word the reader should stop highlighting. */
  ignoreWord: (lemma: string) =>
    request<null>(`/api/reader/words/${encodeURIComponent(lemma)}/ignore`, { method: 'POST' }),

  /** Never throws: the reader shows each outcome under the sentence. */
  translateSentence: async (text: string): Promise<SentenceTranslationResult> => {
    let response: Response

    try {
      response = await fetch('/api/translate/sentence', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text }),
      })
    } catch {
      return { status: 'failed' }
    }

    if (response.ok) {
      try {
        const body = (await response.json()) as { translation: string }
        return { status: 'ok', translation: body.translation }
      } catch {
        return { status: 'failed' }
      }
    }

    if (response.status === 401) {
      unauthorizedHandler()
    }

    if (response.status === 429) {
      return { status: 'limit' }
    }

    if (response.status === 413) {
      return { status: 'tooLong' }
    }

    return response.status === 503 ? { status: 'quota' } : { status: 'failed' }
  },

  getPersonalDictionary: () =>
    request<PersonalDictionary>('/api/dictionaries/personal') as Promise<PersonalDictionary>,

  // 409 (already there) and 400 (unusable input) carry { message }; request() surfaces it as the Error text.
  addPersonalWord: (word: string, translation: string) =>
    request<PersonalWord>('/api/dictionaries/personal/words', {
      method: 'POST',
      body: JSON.stringify({ word, translation }),
    }) as Promise<PersonalWord>,

  removePersonalWord: (wordPairId: number) =>
    request<null>(`/api/dictionaries/personal/words/${wordPairId}`, { method: 'DELETE' }),

  importPersonalWords: (words: BulkWordEntry[]) =>
    request<BulkWordOutcome[]>('/api/dictionaries/personal/words/import', {
      method: 'POST',
      body: JSON.stringify({ words }),
    }) as Promise<BulkWordOutcome[]>,

  /** onUploadProgress gets the bytes sent so far and the body size, as the browser pushes the book out. */
  importDictionary: (
    payload: {
      name: string
      chapters?: ImportChapter[]
      words?: ImportWord[]
      isPublic?: boolean
      fileHash?: string
    },
    onUploadProgress?: UploadProgress,
  ) => uploadJson<ImportResult>('/api/dictionaries/import', payload, onUploadProgress),

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

  // 204 (no words / nothing due / no mistakes) arrives as null — a state, not an error.
  // wordPairIds — "you train what the preview showed"; without them the server takes the same frequency top.
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

  trainingStats: () => request<TrainingStats>('/api/training/stats') as Promise<TrainingStats>,

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

  /** 409 (an unknown family, or nothing to train) throws with the server's message. */
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

  getPronunciationProgress: () =>
    request<PronunciationProgress>('/api/pronunciation/progress') as Promise<PronunciationProgress>,

  getPronunciationFamily: (key: string) =>
    request<PronunciationFamily>(`/api/pronunciation/families/${encodeURIComponent(key)}`) as Promise<PronunciationFamily>,

  nextPronunciationWord: (key: string, includeMastered: boolean) =>
    request<PronunciationNextWord>(
      `/api/pronunciation/families/${encodeURIComponent(key)}/next?includeMastered=${includeMastered}`,
    ) as Promise<PronunciationNextWord>,

  submitPronunciationAttempt: (word: string, accent: Accent, transcript: string) =>
    request<PronunciationAttemptResult>(`/api/pronunciation/words/${encodeURIComponent(word)}/attempts`, {
      method: 'POST',
      body: JSON.stringify({ accent, transcript }),
    }) as Promise<PronunciationAttemptResult>,

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

  // Raw fetch for the same reason as getMe: a refusal here is a boot-time answer, not a
  // session that died under the app.
  telegramWebAppLogin: async (initData: string): Promise<WebAppLogin> => {
    const response = await fetch('/api/auth/telegram/webapp', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ initData }),
    })

    if (response.ok) {
      return { status: 'signed-in', user: (await response.json()) as CurrentUser }
    }

    return { status: response.status === 403 ? 'banned' : 'failed' }
  },

  logout: () => request<null>('/api/auth/logout', { method: 'POST' }),

  /** Answers 409 { message } when refused — the last-admin rule. */
  deleteMe: () => request<null>('/api/auth/me', { method: 'DELETE' }),

  /** Page size is the server's default; the answer says what it was. */
  listUsers: (params: { search?: string; page: number }) => {
    const query = new URLSearchParams()

    if (params.search) {
      query.set('search', params.search)
    }

    query.set('page', String(params.page))

    return request<AdminUserPage>(`/api/admin/users?${query}`) as Promise<AdminUserPage>
  },

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

  /** Ordered by book name, then chapter order; only books the user can still see. */
  getStarredChapters: () => request<StarredChapter[]>('/api/chapters/starred') as Promise<StarredChapter[]>,

  // PUT: starring twice is the same star. 404 = the chapter (or its book) is not visible.
  starChapter: (chapterId: number) => request<null>(`/api/chapters/${chapterId}/star`, { method: 'PUT' }),

  unstarChapter: (chapterId: number) => request<null>(`/api/chapters/${chapterId}/star`, { method: 'DELETE' }),
}
