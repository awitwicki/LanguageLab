import { useCallback, useEffect, useState } from 'react'
import { api, type DictionaryListItem } from './api/client'
import { AppShell } from './layout/AppShell'
import { Sidebar } from './layout/Sidebar'
import { HomeScreen } from './screens/HomeScreen'
import { ImportScreen } from './screens/ImportScreen'
import { DictionaryScreen } from './screens/DictionaryScreen'
import { SortingScreen } from './screens/SortingScreen'

type Route =
  | { name: 'home' }
  | { name: 'import' }
  | { name: 'dictionary'; id: number }
  | { name: 'sorting'; id: number; chapterIds: number[] | null; scopeTitle: string }

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

  // Прогрес у сайдбарі має відображати щойно посортоване, а не стан на момент
  // старту — тому список перезавантажується на кожній зміні маршруту.
  const routeKey = 'id' in route ? `${route.name}:${route.id}` : route.name

  useEffect(() => {
    void reload()
  }, [reload, routeKey])

  const activeId = 'id' in route ? route.id : null
  const activeName = dictionaries?.find((d) => d.id === activeId)?.name ?? 'Словник'

  return (
    <AppShell
      onHome={() => setRoute({ name: 'home' })}
      sidebar={
        <Sidebar
          items={dictionaries}
          error={listError}
          activeId={activeId}
          importActive={route.name === 'import'}
          onSelect={(id) => setRoute({ name: 'dictionary', id })}
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

      {route.name === 'import' && (
        <ImportScreen onImported={(id) => setRoute({ name: 'dictionary', id })} />
      )}

      {route.name === 'dictionary' && (
        <DictionaryScreen
          id={route.id}
          onSort={(chapterIds, scopeTitle) =>
            setRoute({ name: 'sorting', id: route.id, chapterIds, scopeTitle })
          }
        />
      )}

      {route.name === 'sorting' && (
        <SortingScreen
          dictionaryId={route.id}
          dictionaryName={activeName}
          chapterIds={route.chapterIds}
          scopeTitle={route.scopeTitle}
          onBack={() => setRoute({ name: 'dictionary', id: route.id })}
        />
      )}
    </AppShell>
  )
}
