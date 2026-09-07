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
      <h1 className="large-title">{hasDictionaries ? 'Обери словник' : 'Почнімо з книжки'}</h1>

      {hasDictionaries ? (
        <p className="welcome-hint">
          Словники — у бічній панелі. Відкрий будь-який, щоб побачити статистику й почати сортування.
        </p>
      ) : (
        <>
          <p className="welcome-hint">
            Тут поки порожньо. Імпортуй книжку у форматі .fb2 — слова розкладуться по главах, і їх
            можна буде сортувати на «знаю» та «не знаю».
          </p>
          <button type="button" className="btn btn-primary btn-lg" onClick={onImport}>
            Імпортувати книжку
          </button>
        </>
      )}
    </section>
  )
}
