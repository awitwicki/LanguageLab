import { useEffect, useState } from 'react'
import { decodeClip, isAudioSupported } from './decodeClip'
import { analyzeClip, type ClipAnalysis } from './spectrogram'

export type ClipView =
  | { status: 'loading' }
  | { status: 'ready'; analysis: ClipAnalysis }
  | { status: 'failed' }
  | { status: 'unsupported' }

export function useClipAnalysis(url: string | null): ClipView | null {
  const [settled, setSettled] = useState<{ url: string; view: ClipView } | null>(null)
  const supported = isAudioSupported()

  useEffect(() => {
    if (url === null || !supported) return
    let stale = false
    decodeClip(url)
      .then((clip) => {
        if (!stale) setSettled({ url, view: { status: 'ready', analysis: analyzeClip(clip.samples, clip.sampleRate) } })
      })
      .catch(() => {
        if (!stale) setSettled({ url, view: { status: 'failed' } })
      })
    return () => {
      stale = true
    }
  }, [url, supported])

  if (url === null) return null
  if (!supported) return { status: 'unsupported' }
  return settled?.url === url ? settled.view : { status: 'loading' }
}
