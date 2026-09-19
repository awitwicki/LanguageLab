import { afterEach, describe, expect, it } from 'vitest'
import { createSpeechRecognition, isSpeechRecognitionSupported } from './speechRecognition'

afterEach(() => {
  delete window.SpeechRecognition
  delete window.webkitSpeechRecognition
})

describe('isSpeechRecognitionSupported', () => {
  it('is false when neither constructor exists', () => {
    expect(isSpeechRecognitionSupported()).toBe(false)
  })

  it('is true when SpeechRecognition exists', () => {
    window.SpeechRecognition = class {} as never
    expect(isSpeechRecognitionSupported()).toBe(true)
  })

  it('is true when only webkitSpeechRecognition exists', () => {
    window.webkitSpeechRecognition = class {} as never
    expect(isSpeechRecognitionSupported()).toBe(true)
  })
})

describe('createSpeechRecognition', () => {
  it('returns null when unsupported', () => {
    expect(createSpeechRecognition('en-US')).toBeNull()
  })

  it('constructs and configures an instance when supported', () => {
    let constructedWithLang = ''
    class FakeRecognition {
      lang = ''
      continuous = false
      interimResults = false
      maxAlternatives = 1
      start() {}
      stop() {}
    }
    window.SpeechRecognition = FakeRecognition as never

    const recognition = createSpeechRecognition('en-GB')
    constructedWithLang = recognition!.lang

    expect(recognition).not.toBeNull()
    expect(constructedWithLang).toBe('en-GB')
  })
})
