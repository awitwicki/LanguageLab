import { flattenChapters, type ChapterMode, type SectionNode } from '../fb2/chapters'
import { aggregate, type AggregatedChapter } from '../fb2/aggregate'

// decodeFb2/parseBook run on the main thread, not here: they need DOMParser, which
// real-browser testing showed is unavailable in this browser's Worker scope (Chrome
// exposes it on Window, not inside workers). They're fast — native XML parsing of a
// multi-MB file is well under a second — so running them synchronously on the main
// thread doesn't freeze the UI. Only the genuinely slow step, lemmatizing the whole
// book's vocabulary, stays here, off the main thread.
export type WorkerRequest = { kind: 'aggregate'; sections: SectionNode[]; mode: ChapterMode }

export type WorkerResponse =
  | { kind: 'aggregated'; chapters: AggregatedChapter[] }
  | { kind: 'error'; message: string }

self.onmessage = (event: MessageEvent<WorkerRequest>) => {
  try {
    const chapters = aggregate(flattenChapters(event.data.sections, event.data.mode))
    const response: WorkerResponse = { kind: 'aggregated', chapters }
    self.postMessage(response)
  } catch (error) {
    const response: WorkerResponse = {
      kind: 'error',
      message: error instanceof Error ? error.message : String(error),
    }
    self.postMessage(response)
  }
}
