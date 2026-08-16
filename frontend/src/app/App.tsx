import { useTranslation } from 'react-i18next'
import { LanguageSwitcher } from '../components/LanguageSwitcher'
import { useDirection } from '../i18n/useDirection'

export const App = () => {
  const { t } = useTranslation()
  useDirection()

  return (
    <>
      <header>
        <h1>{t('app.title')}</h1>
        <LanguageSwitcher />
      </header>
      <main />
    </>
  )
}
