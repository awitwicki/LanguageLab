import { useCallback, useEffect, useState } from 'react'
import { api, type DictionaryListItem, type TrainingStarted } from './api/client'
import { AppShell } from './layout/AppShell'
import { Sidebar } from './layout/Sidebar'
import { HomeScreen } from './screens/HomeScreen'
import { ImportScreen } from './screens/ImportScreen'
import { DictionaryScreen } from './screens/DictionaryScreen'
import { SortingScreen } from './screens/SortingScreen'
import { TrainingStartScreen } from './screens/TrainingStartScreen'
import { TrainingScreen } from './screens/TrainingScreen'

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

const REVIEW_TITLE = 'Повторення'

export default function App() {
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

  // Словник, до якого належить поточний екран: підсвітка в сайдбарі й «назад».
  const activeId = 'id' in route ? route.id : 'dictionaryId' in route ? route.dictionaryId : null

  // Прогрес у сайдбарі має відображати щойно посортоване, а не стан на момент
  // старту — тому список перезавантажується на кожній зміні маршруту.
  const routeKey = activeId === null ? route.name : `${route.name}:${activeId}`

  useEffect(() => {
    void reload()
  }, [reload, routeKey])

  const activeName = dictionaries?.find((d) => d.id === activeId)?.name ?? 'Словник'

  const openDictionary = (id: number) => setRoute({ name: 'dictionary', id })

  return (
    <AppShell
      onHome={() => setRoute({ name: 'home' })}
      sidebar={
        <Sidebar
          items={dictionaries}
          error={listError}
          activeId={activeId}
          importActive={route.name === 'import'}
          onSelect={openDictionary}
          onImport={() => setRoute({ name: 'import' })}
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
          onReview={(started) =>
            setRoute({
              name: 'training',
              dictionaryId: route.id,
              scopeTitle: REVIEW_TITLE,
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
    </AppShell>
  )
}
