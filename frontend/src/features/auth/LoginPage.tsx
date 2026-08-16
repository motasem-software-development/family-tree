import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../services/apiClient'
import { useAuth } from './AuthContext'

export const LoginPage = () => {
  const { t } = useTranslation()
  const { login } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setErrorCode(null)
    setSubmitting(true)
    try {
      await login(email, password)
    } catch (error) {
      // Translate the stable code; never render a server-supplied string.
      setErrorCode(error instanceof ApiError ? error.code : 'NETWORK')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={onSubmit}>
      <h1>{t('auth.signIn')}</h1>

      <label htmlFor="email">{t('auth.email')}</label>
      <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />

      <label htmlFor="password">{t('auth.password')}</label>
      <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />

      {errorCode && <p role="alert">{t(`errors.${errorCode}`, { defaultValue: t('errors.UNKNOWN') })}</p>}

      <button type="submit" disabled={submitting}>
        {submitting ? t('auth.signingIn') : t('auth.signIn')}
      </button>
    </form>
  )
}
