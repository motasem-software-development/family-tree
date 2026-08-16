import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import type { Language } from './index'

export type Direction = 'rtl' | 'ltr'

export const directionFor = (language: string): Direction =>
  language.startsWith('ar') ? 'rtl' : 'ltr'

/**
 * Keeps <html dir> and <html lang> in step with the active language.
 * Layout follows the document direction, so no component needs its own RTL branch.
 */
export const useDirection = (): Direction => {
  const { i18n } = useTranslation()
  const direction = directionFor(i18n.language as Language)

  useEffect(() => {
    document.documentElement.dir = direction
    document.documentElement.lang = i18n.language
    // The tree canvas scales from the reading edge, so zooming grows away from the root
    // rather than dragging it off-screen. transform-origin has no logical-property form.
    document.documentElement.style.setProperty(
      '--origin-x',
      direction === 'rtl' ? 'right' : 'left',
    )
  }, [direction, i18n.language])

  return direction
}
