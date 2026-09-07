import { useState } from 'react'
import { HomeScreen } from './screens/HomeScreen'
import { ImportScreen } from './screens/ImportScreen'
import { DictionaryScreen } from './screens/DictionaryScreen'
import { SortingScreen } from './screens/SortingScreen'
import './app.css'

type Route =
  | { name: 'home' }
  | { name: 'import' }
  | { name: 'dictionary'; id: number }
  | { name: 'sorting'; id: number; chapterIds: number[] | null }

export default function App() {
  const [route, setRoute] = useState<Route>({ name: 'home' })

  return (
    <main>
      {route.name === 'home' && (
        <HomeScreen
          onOpen={(id) => setRoute({ name: 'dictionary', id })}
          onImport={() => setRoute({ name: 'import' })}
        />
      )}

      {route.name === 'import' && (
        <ImportScreen onImported={(id) => setRoute({ name: 'dictionary', id })} />
      )}

      {route.name === 'dictionary' && (
        <DictionaryScreen
          id={route.id}
          onBack={() => setRoute({ name: 'home' })}
          onSort={(chapterIds) => setRoute({ name: 'sorting', id: route.id, chapterIds })}
        />
      )}

      {route.name === 'sorting' && (
        <SortingScreen
          dictionaryId={route.id}
          chapterIds={route.chapterIds}
          onBack={() => setRoute({ name: 'dictionary', id: route.id })}
        />
      )}
    </main>
  )
}
