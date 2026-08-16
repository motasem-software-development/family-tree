import { useTranslation } from 'react-i18next'
import { useDirection } from '../i18n/useDirection'

export const LanguageSwitcher = () => {
  const { i18n, t } = useTranslation()
  useDirection()

  const next = i18n.language.startsWith('ar') ? 'en' : 'ar'

  return (
    <button type="button" onClick={() => void i18n.changeLanguage(next)}>
      {t(`language.${next}`)}
    </button>
  )
}
