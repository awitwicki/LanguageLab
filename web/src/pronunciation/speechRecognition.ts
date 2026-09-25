export interface SpeechRecognitionResultEvent extends Event {
  results: SpeechRecognitionResultListLike
}

/** The `error` field carries the failure code ('not-allowed', 'no-speech', 'network', …). */
export interface SpeechRecognitionErrorEvent extends Event {
  readonly error: string
}

interface SpeechRecognitionResultListLike {
  readonly length: number
  [index: number]: { readonly length: number; [index: number]: { transcript: string } }
}

export interface SpeechRecognitionLike extends EventTarget {
  lang: string
  continuous: boolean
  interimResults: boolean
  maxAlternatives: number
  start: () => void
  stop: () => void
  /** Unlike stop(), discards whatever was captured: no onresult follows. */
  abort: () => void
  onresult: ((event: SpeechRecognitionResultEvent) => void) | null
  onerror: ((event: SpeechRecognitionErrorEvent) => void) | null
  onend: (() => void) | null
}

interface SpeechRecognitionConstructor {
  new (): SpeechRecognitionLike
}

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor
    webkitSpeechRecognition?: SpeechRecognitionConstructor
  }
}

function constructorRef(): SpeechRecognitionConstructor | undefined {
  return window.SpeechRecognition ?? window.webkitSpeechRecognition
}

export function isSpeechRecognitionSupported(): boolean {
  return constructorRef() !== undefined
}

export function createSpeechRecognition(lang: string): SpeechRecognitionLike | null {
  const Ctor = constructorRef()
  if (!Ctor) {
    return null
  }

  const recognition = new Ctor()
  recognition.lang = lang
  recognition.continuous = false
  recognition.interimResults = false
  recognition.maxAlternatives = 1
  return recognition
}

export function transcriptOf(event: SpeechRecognitionResultEvent): string {
  return event.results[0]?.[0]?.transcript ?? ''
}
