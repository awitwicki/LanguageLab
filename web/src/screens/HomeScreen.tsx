import './HomeScreen.css'

interface Props {
  hasDictionaries: boolean
  onImport: () => void
}

/// Список словників живе в сайдбарі, тож «домівка» — це порожній стан:
/// або підказка обрати словник, або запрошення імпортувати першу книжку.
export function HomeScreen({ hasDictionaries, onImport }: Props) {
  return (
    <section className="welcome">
      <h1 className="large-title">{hasDictionaries ? 'Pick a dictionary' : 'Start with a book'}</h1>

      {hasDictionaries ? (
        <p className="welcome-hint">
          Your dictionaries are in the sidebar. Open one to see its stats and start sorting.
        </p>
      ) : (
        <>
          <p className="welcome-hint">
            Nothing here yet. Import an .fb2 book — its words are split by chapter, ready to be
            sorted into “know” and “don’t know”.
          </p>
          <button type="button" className="btn btn-primary btn-lg" onClick={onImport}>
            Import a book
          </button>
        </>
      )}
    </section>
  )
}
