import { useCallback, useEffect, useState } from 'react'
import { api, type DictionaryListItem, type IrregularVerbsOverview, type TrainingStarted } from './api/client'
import { useAuth } from './auth/useAuth'
import { AppShell } from './layout/AppShell'
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
import { VerbSessionScreen } from './verbs/VerbSessionScreen'
import { VerbsScreen } from './verbs/VerbsScreen'

type Route =
  | { name: 'home' }
  | { name: 'import' }
  | { name: 'dictionary'; id: number }
  | { name: 'sorting'; id: number; chapterIds: number[] | null; scopeTitle: string }
  | { name: 'training-start'; dictionaryId: number; chapterIds: number[] | null; scopeTitle: string }
  | {
      name: 'training'
      dictionaryId: number
      scopeTitle: string
      chapterIds: number[] | null
      batchSize: number | null
      started: TrainingStarted
    }
  | { name: 'verbs' }
  | { name: 'verbs-session'; step: number; run: number }
  | { name: 'admin' }

const REVIEW_TITLE = 'Review'

export default function App() {
  const { state, loginFailed, signOut, deleteAccount, dismissBanned } = useAuth()

  const [route, setRoute] = useState<Route>({ name: 'home' })
  const [dictionaries, setDictionaries] = useState<DictionaryListItem[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const [verbsOverview, setVerbsOverview] = useState<IrregularVerbsOverview | null>(null)

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

  // Best-effort: the sidebar caption just falls back to "0 of 4 steps" if this fails,
  // so a failure here should not surface as the dictionaries list's error banner.
  const reloadVerbsOverview = useCallback(() => api.getIrregularVerbs().then(setVerbsOverview).catch(() => {}), [])

  // Словник, до якого належить поточний екран: підсвітка в сайдбарі й «назад».
  const activeId = 'id' in route ? route.id : 'dictionaryId' in route ? route.dictionaryId : null

  // Прогрес у сайдбарі має відображати щойно посортоване, а не стан на момент
  // старту — тому список перезавантажується на кожній зміні маршруту.
  const routeKey = activeId === null ? route.name : `${route.name}:${activeId}`

  useEffect(() => {
    if (state.status !== 'signed-in') {
      return
    }

    void reload()
    void reloadVerbsOverview()
  }, [reload, reloadVerbsOverview, routeKey, state.status])

  const activeName = dictionaries?.find((d) => d.id === activeId)?.name ?? 'Dictionary'
  const verbsDoneSteps = verbsOverview?.steps.filter((s) => s.learnedVerbs === s.totalVerbs).length ?? 0
  const verbsTotalSteps = verbsOverview?.steps.length ?? 4
  const verbsActive = route.name === 'verbs' || route.name === 'verbs-session'

  const openDictionary = (id: number) => setRoute({ name: 'dictionary', id })

  // The shell only makes sense for someone signed in: the sidebar lists their dictionaries
  // and every API call behind it needs the cookie.
  if (state.status === 'loading') {
    return <main className="boot" aria-busy="true" />
  }

  if (state.status === 'banned') {
    return <BannedScreen onBack={dismissBanned} />
  }

  if (state.status === 'anonymous') {
    return <LoginScreen loginFailed={loginFailed} />
  }

  return (
    <AppShell
      user={state.user}
      onHome={() => setRoute({ name: 'home' })}
      onAdmin={() => setRoute({ name: 'admin' })}
      onSignOut={() => void signOut()}
      onDeleteAccount={deleteAccount}
      sidebar={
        <Sidebar
          items={dictionaries}
          error={listError}
          activeId={activeId}
          importActive={route.name === 'import'}
          canImport={state.user.role === 'admin'}
          onSelect={openDictionary}
          onImport={() => setRoute({ name: 'import' })}
          verbsDoneSteps={verbsDoneSteps}
          verbsTotalSteps={verbsTotalSteps}
          verbsActive={verbsActive}
          onOpenVerbs={() => setRoute({ name: 'verbs' })}
        />
      }
    >
      {route.name === 'home' && (
        <HomeScreen
          hasDictionaries={(dictionaries?.length ?? 0) > 0}
          onImport={() => setRoute({ name: 'import' })}
        />
      )}

      {route.name === 'import' && <ImportScreen onImported={openDictionary} />}

      {route.name === 'dictionary' && (
        <DictionaryScreen
          id={route.id}
          onSort={(chapterIds, scopeTitle) =>
            setRoute({ name: 'sorting', id: route.id, chapterIds, scopeTitle })
          }
          onTrain={(chapterIds, scopeTitle) =>
            setRoute({ name: 'training-start', dictionaryId: route.id, chapterIds, scopeTitle })
          }
          onReview={(started, scopeTitle) =>
            setRoute({
              name: 'training',
              dictionaryId: route.id,
              scopeTitle: scopeTitle ? `${REVIEW_TITLE} · ${scopeTitle}` : REVIEW_TITLE,
              chapterIds: null,
              batchSize: null,
              started,
            })
          }
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
          dictionaryName={activeName}
          scopeTitle={route.scopeTitle}
          chapterIds={route.chapterIds}
          batchSize={route.batchSize}
          started={route.started}
          onBack={() => openDictionary(route.dictionaryId)}
        />
      )}

      {route.name === 'admin' && <AdminScreen meId={state.user.id} />}

      {route.name === 'verbs' && (
        <VerbsScreen onStartSession={(step) => setRoute({ name: 'verbs-session', step, run: 0 })} />
      )}

      {/* `run` remounts the screen for Continue: a fresh plan from the grades just posted. */}
      {route.name === 'verbs-session' && (
        <VerbSessionScreen
          key={`${route.step}:${route.run}`}
          step={route.step}
          onBack={() => setRoute({ name: 'verbs' })}
          onContinue={() => setRoute({ name: 'verbs-session', step: route.step, run: route.run + 1 })}
        />
      )}
    </AppShell>
  )
}
