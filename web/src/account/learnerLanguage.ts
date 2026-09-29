import { createContext, useContext } from 'react'
import type { CurrentUser } from '../api/client'

export const DEFAULT_LANGUAGE = 'uk'

/** The learner's language code, for lang="" on translated text. Provided once in App. */
export const LearnerLanguageContext = createContext<string>(DEFAULT_LANGUAGE)

export const useLearnerLanguage = () => useContext(LearnerLanguageContext)

/** What the picker starts on: the current language, else Telegram's suggestion, else Ukrainian. */
export function preselect(user: Pick<CurrentUser, 'language' | 'suggestedLanguage'>): string {
  return user.language ?? user.suggestedLanguage ?? DEFAULT_LANGUAGE
}
