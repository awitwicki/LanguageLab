import { useCallback, useEffect, useRef } from 'react'
import { decodeAudio, type Clip } from './decodeClip'

export interface LiveFrame {
  bins: Uint8Array
  binHz: number
}

export interface MicCapture {
  /** Rejects with the getUserMedia error (NotAllowedError when the user refuses). */
  start: () => Promise<void>
  liveFrame: () => LiveFrame | null
  /** Releases the microphone; resolves the recording as mono PCM, or null when there is none. */
  stop: () => Promise<Clip | null>
}

interface Session {
  stream: MediaStream
  context: AudioContext
  analyser: AnalyserNode
  recorder: MediaRecorder
  chunks: Blob[]
  bins: Uint8Array<ArrayBuffer> // getByteFrequencyData only accepts the ArrayBuffer-backed variant
}

const FFT_SIZE = 1024

export function isMicSupported(): boolean {
  return (
    typeof AudioContext !== 'undefined' &&
    typeof MediaRecorder !== 'undefined' &&
    typeof navigator !== 'undefined' &&
    typeof navigator.mediaDevices?.getUserMedia === 'function'
  )
}

function release(session: Session): Promise<void> {
  for (const track of session.stream.getTracks()) track.stop()
  return session.context.close()
}

function stopRecorder(recorder: MediaRecorder, chunks: Blob[]): Promise<Blob> {
  return new Promise((resolve) => {
    const finish = () => resolve(new Blob(chunks, { type: recorder.mimeType }))
    if (recorder.state === 'inactive') {
      finish()
      return
    }
    recorder.onstop = finish
    recorder.stop()
  })
}

export function useMicCapture(): MicCapture {
  const session = useRef<Session | null>(null)
  const unmounted = useRef(false)

  useEffect(() => {
    unmounted.current = false
    return () => {
      unmounted.current = true
      const current = session.current
      session.current = null
      if (current) void release(current)
    }
  }, [])

  const start = useCallback(async () => {
    if (session.current) return
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    if (unmounted.current) {
      for (const track of stream.getTracks()) track.stop()
      return
    }
    const context = new AudioContext()
    const analyser = context.createAnalyser()
    analyser.fftSize = FFT_SIZE
    analyser.smoothingTimeConstant = 0
    // Match the frozen spectrogram's 60 dB peak-normalized range (spectrogram.ts's
    // DB_RANGE), so the live scroll and the frozen strip look equally bright for the
    // same recording instead of the default -100..-30 dBFS window.
    analyser.minDecibels = -100
    analyser.maxDecibels = -40
    context.createMediaStreamSource(stream).connect(analyser)

    const chunks: Blob[] = []
    const recorder = new MediaRecorder(stream)
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunks.push(event.data)
    }
    recorder.start()

    session.current = { stream, context, analyser, recorder, chunks, bins: new Uint8Array(analyser.frequencyBinCount) }
  }, [])

  const liveFrame = useCallback((): LiveFrame | null => {
    const current = session.current
    if (!current) return null
    current.analyser.getByteFrequencyData(current.bins)
    return { bins: current.bins, binHz: current.context.sampleRate / FFT_SIZE }
  }, [])

  const stop = useCallback(async (): Promise<Clip | null> => {
    const current = session.current
    if (!current) return null
    session.current = null

    const blob = await stopRecorder(current.recorder, current.chunks)
    await release(current)
    if (blob.size === 0) return null
    try {
      return await decodeAudio(await blob.arrayBuffer())
    } catch {
      return null
    }
  }, [])

  return { start, liveFrame, stop }
}
