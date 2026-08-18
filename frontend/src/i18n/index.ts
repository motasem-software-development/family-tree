import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import { PASSWORD_MINIMUM_LENGTH } from '../config/passwordPolicy'
import ar from './locales/ar.json'
import en from './locales/en.json'

export const SUPPORTED_LANGUAGES = ['ar', 'en'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

void i18n.use(initReactI18next).init({
  resources: {
    ar: { translation: ar },
    en: { translation: en },
  },
  lng: 'ar',
  fallbackLng: 'en',
  interpolation: {
    escapeValue: false,
    // Supplied to every message in both languages, so the messages that quote the password
    // minimum interpolate it without any call site having to pass it — and the number itself
    // is written down exactly once in the frontend (src/config/passwordPolicy.ts).
    defaultVariables: { passwordMinimum: PASSWORD_MINIMUM_LENGTH },
  },
})

export default i18n
