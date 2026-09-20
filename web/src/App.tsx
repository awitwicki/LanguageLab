import { useCallback, useEffect, useState } from 'react'
import { api, type DictionaryListItem, type SessionStarted, type TrainingStarted } from './api/client'
import { useAuth } from './auth/useAuth'
import { AppShell } from './layout/AppShell'
import { modeOf, type AppMode } from './layout/mode'
import { Sidebar } from './layout/Sidebar'
import { HomeScreen } from './screens/HomeScreen'
import { ImportScreen } from './screens/ImportScreen'
import { DictionaryScreen } from './screens/DictionaryScreen'
import { SortingScreen } from './screens/SortingScreen'
import { TrainingStartScreen } from './screens/TrainingStartScreen'
import { TrainingScreen } from './screens/TrainingScreen'
import { LoginScreen } from './screens/LoginScreen'
import { BannedScreen } from './screens/BannedScreen'
import { AdminScreen } from './screens/AdminScreen'
import { PersonalDictionaryScreen } from './screens/PersonalDictionaryScreen'
import { VerbGroupScreen } from './verbs/VerbGroupScreen'
import { VerbSessionScreen } from './verbs/VerbSessionScreen'
import { VerbsScreen } from './verbs/VerbsScreen'
import { PronunciationFamiliesScreen } from './pronunciation/PronunciationFamiliesScreen'
import { PronunciationFamilyScreen } from './pronunciation/PronunciationFamilyScreen'

type Route =
  | { name: 'home' }
  | { name: 'import' }
  | { name: 'dictionary'; id: number }
  | { name: 'personal' }
  | { name: 'sorting'; id: number; chapterIds: number[] | null; scopeTitle: string }
  | { name: 'training-start'; dictionaryId: number; chapterIds: number[] | null; scopeTitle: string }
  | {
      name: 'training'
      /** null for a review across every book, started from the home screen. */
      dictionaryId: number | null
      scopeTitle: string
      chapterIds: number[] | null
      batchSize: number | null
      started: TrainingStarted
    }
  | { name: 'verbs' }
  | { name: 'verbs-group'; group: number }
  | { name: 'verbs-session'; sessionId: number }
  | { name: 'pronunciation' }
  | { name: 'pronunciation-family'; key: string }
  | { name: 'admin' }

const REVIEW_TITLE = 'Review'
const ALL_WORDS = 'All words'
const ALL_DICTIONARIES = 'All dictionaries'

const MODE_LANDING: Record<AppMode, Route> = {
  words: { name: 'home' },
  pronunciation: { name: 'pronunciation' },
  verbs: { name: 'verbs' },
}

export default function App() {
  const { state, loginFailed, insideTelegram, signInWithTelegram, signOut, deleteAccount, dismissBanned } = useAuth()

  const [route, setRoute] = useState<Route>({ name: 'home' })
  const [dictionaries, setDictionaries] = useState<DictionaryListItem[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)

  const reload = useCallback(
    () =>
      api
        .listDictionaries()
        .then((items) => {
          setDictionaries(items)
          setListError(null)
        })
        .catch((e) => setListError(String(e))),
    [],
  )

  // The dictionary the current screen belongs to: sidebar highlight and "back".
  const activeId = 'id' in route ? route.id : 'dictionaryId' in route ? route.dictionaryId : null

  // The sidebar progress must reflect what was just sorted, not the state at
  // startup — so the list reloads on every route change.
  const routeKey = activeId === null ? route.name : `${route.name}:${activeId}`

  useEffect(() => {
    if (state.status !== 'signed-in') {
      return
    }

    void reload()
  }, [reload, routeKey, state.status])

  const activeName = dictionaries?.find((d) => d.id === activeId)?.name ?? 'Dictionary'
  const mode = modeOf(route.name)

  // The personal dictionary has its own screen: coming back from its exercises must land
  // there, not on the book screen.
  const openDictionary = (id: number) =>
    setRoute(dictionaries?.find((d) => d.id === id)?.isPersonal ? { name: 'personal' } : { name: 'dictionary', id })

  const startVerbSession = (started: SessionStarted) => setRoute({ name: 'verbs-session', sessionId: started.id })

  // A chapter (or the whole book) can be sorted, trained or reviewed from the book page and,
  // for starred chapters, from the home screen — the same three transitions either way.
  const sortScope = (dictionaryId: number, chapterIds: number[] | null, scopeTitle: string) =>
    setRoute({ name: 'sorting', id: dictionaryId, chapterIds, scopeTitle })

  const trainScope = (dictionaryId: number, chapterIds: number[] | null, scopeTitle: string) =>
    setRoute({ name: 'training-start', dictionaryId, chapterIds, scopeTitle })

  const reviewScope = (started: TrainingStarted, dictionaryId: number, scopeTitle?: string) =>
    setRoute({
      name: 'training',
      dictionaryId,
      scopeTitle: scopeTitle ? `${REVIEW_TITLE} · ${scopeTitle}` : REVIEW_TITLE,
      chapterIds: null,
      batchSize: null,
      started,
    })

  // The shell only makes sense for someone signed in: the sidebar lists their dictionaries
  // and every API call behind it needs the cookie.
  if (state.status === 'loading') {
    return <main className="boot" aria-busy="true" />
  }

  if (state.status === 'banned') {
    return <BannedScreen onBack={dismissBanned} />
  }

  if (state.status === 'anonymous') {
    return (
      <LoginScreen
        loginFailed={loginFailed}
        onTelegramSignIn={insideTelegram ? () => void signInWithTelegram() : null}
      />
    )
  }

  return (
    <AppShell
      user={state.user}
      mode={mode}
      onSelectMode={(next) => setRoute(MODE_LANDING[next])}
      screenKey={routeKey}
      onHome={() => setRoute({ name: 'home' })}
      onAdmin={() => setRoute({ name: 'admin' })}
      onSignOut={() => void signOut()}
      onDeleteAccount={deleteAccount}
      sidebar={
        mode === 'words' ? (
          <Sidebar
            items={dictionaries}
            error={listError}
            activeId={activeId}
            importActive={route.name === 'import'}
            canImport={state.user.role === 'admin'}
            onSelect={openDictionary}
            onImport={() => setRoute({ name: 'import' })}
            personalActive={route.name === 'personal'}
            onOpenPersonal={() => setRoute({ name: 'personal' })}
          />
        ) : null
      }
    >
      {route.name === 'home' && (
        <HomeScreen
          hasDictionaries={(dictionaries?.filter((d) => !d.isPersonal).length ?? 0) > 0}
          onImport={() => setRoute({ name: 'import' })}
          onReview={(started) =>
            setRoute({
              name: 'training',
              dictionaryId: null,
              scopeTitle: REVIEW_TITLE,
              chapterIds: null,
              batchSize: null,
              started,
            })
          }
          onSort={sortScope}
          onTrain={trainScope}
          onChapterReview={reviewScope}
        />
      )}

      {route.name === 'import' && <ImportScreen onImported={openDictionary} />}

      {route.name === 'personal' && (
        <PersonalDictionaryScreen
          onTrain={(dictionaryId) =>
            setRoute({ name: 'training-start', dictionaryId, chapterIds: null, scopeTitle: ALL_WORDS })
          }
          onReview={(started, dictionaryId) =>
            setRoute({
              name: 'training',
              dictionaryId,
              scopeTitle: REVIEW_TITLE,
              chapterIds: null,
              batchSize: null,
              started,
            })
          }
          onChanged={() => void reload()}
        />
      )}

      {route.name === 'dictionary' && (
        <DictionaryScreen
          id={route.id}
          role={state.user.role}
          onSort={(chapterIds, scopeTitle) => sortScope(route.id, chapterIds, scopeTitle)}
          onTrain={(chapterIds, scopeTitle) => trainScope(route.id, chapterIds, scopeTitle)}
          onReview={(started, scopeTitle) => reviewScope(started, route.id, scopeTitle)}
          onDeleted={() => {
            setRoute({ name: 'home' })
            void reload()
          }}
        />
      )}

      {route.name === 'sorting' && (
        <SortingScreen
          dictionaryId={route.id}
          dictionaryName={activeName}
          chapterIds={route.chapterIds}
          scopeTitle={route.scopeTitle}
          onBack={() => openDictionary(route.id)}
        />
      )}

      {route.name === 'training-start' && (
        <TrainingStartScreen
          dictionaryId={route.dictionaryId}
          dictionaryName={activeName}
          chapterIds={route.chapterIds}
          scopeTitle={route.scopeTitle}
          onStarted={(started, batchSize) =>
            setRoute({
              name: 'training',
              dictionaryId: route.dictionaryId,
              scopeTitle: route.scopeTitle,
              chapterIds: route.chapterIds,
              batchSize,
              started,
            })
          }
          onBack={() => openDictionary(route.dictionaryId)}
        />
      )}

      {route.name === 'training' && (
        <TrainingScreen
          key={route.started.trainingId}
          dictionaryId={route.dictionaryId}
          dictionaryName={route.dictionaryId === null ? ALL_DICTIONARIES : activeName}
          scopeTitle={route.scopeTitle}
          chapterIds={route.chapterIds}
          batchSize={route.batchSize}
          started={route.started}
          onBack={() => {
            if (route.dictionaryId === null) setRoute({ name: 'home' })
            else openDictionary(route.dictionaryId)
          }}
        />
      )}

      {route.name === 'admin' && <AdminScreen meId={state.user.id} />}

      {route.name === 'verbs' && (
        <VerbsScreen
          onOpenGroup={(group) => setRoute({ name: 'verbs-group', group })}
          onStartSession={startVerbSession}
        />
      )}

      {route.name === 'verbs-group' && (
        <VerbGroupScreen
          group={route.group}
          onBack={() => setRoute({ name: 'verbs' })}
          onStartSession={startVerbSession}
        />
      )}

      {route.name === 'verbs-session' && (
        <VerbSessionScreen
          key={route.sessionId}
          sessionId={route.sessionId}
          onBack={() => setRoute({ name: 'verbs' })}
          onRepeatErrors={startVerbSession}
        />
      )}

      {route.name === 'pronunciation' && (
        <PronunciationFamiliesScreen onOpenFamily={(key) => setRoute({ name: 'pronunciation-family', key })} />
      )}

      {route.name === 'pronunciation-family' && (
        <PronunciationFamilyScreen
          key={route.key}
          familyKey={route.key}
          onBack={() => setRoute({ name: 'pronunciation' })}
        />
      )}
    </AppShell>
  )
}
