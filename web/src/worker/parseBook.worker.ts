import { flattenChapters, type ChapterMode, type SectionNode } from '../fb2/chapters'
import { aggregate, type AggregatedChapter } from '../fb2/aggregate'

// decodeFb2/parseBook run on the main thread, not here: they need DOMParser, which
// real-browser testing showed is unavailable in this browser's Worker scope (Chrome
// exposes it on Window, not inside workers). They're fast — native XML parsing of a
// multi-MB file is well under a second — so running them synchronously on the main
// thread doesn't freeze the UI. Only the genuinely slow step, lemmatizing the whole
// book's vocabulary, stays here, off the main thread.
export type WorkerRequest = { kind: 'aggregate'; sections: SectionNode[]; mode: ChapterMode }

/**
 * 'progress' arrives after every lemmatized chapter (and once, at 0, as soon as the request is
 * picked up — the screen learns the worker is alive before the first chapter is done);
 * 'aggregated' or 'error' closes the request.
 */
export type WorkerResponse =
  | { kind: 'progress'; done: number; total: number }
  | { kind: 'aggregated'; chapters: AggregatedChapter[] }
  | { kind: 'error'; message: string }

function post(response: WorkerResponse) {
  self.postMessage(response)
}

self.onmessage = (event: MessageEvent<WorkerRequest>) => {
  try {
    const chapters = flattenChapters(event.data.sections, event.data.mode)

    post({ kind: 'progress', done: 0, total: chapters.length })

    const aggregated = aggregate(chapters, (done, total) => post({ kind: 'progress', done, total }))

    post({ kind: 'aggregated', chapters: aggregated })
  } catch (error) {
    post({ kind: 'error', message: error instanceof Error ? error.message : String(error) })
  }
}
