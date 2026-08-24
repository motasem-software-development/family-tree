import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate } from 'react-router-dom'
import { ApiError } from '../../services/apiClient'
import { useAuth } from './AuthContext'

const FamilyIcon = ({ size = 22 }: { size?: number }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    aria-hidden="true"
  >
    <rect x="9" y="3" width="6" height="4" rx="1" />
    <path d="M12 7v5M6 20v-4M18 20v-4M6 16h12" />
  </svg>
)

const fieldStyle: CSSProperties = {
  width: '100%',
  height: 'var(--control-h-lg)',
  padding: '0 12px',
  border: '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  color: 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 14,
}

const labelStyle: CSSProperties = {
  display: 'block',
  marginBottom: 'var(--space-2)',
  fontSize: 13,
  fontWeight: 500,
  color: 'var(--text-2)',
}

export const LoginPage = () => {
  const { t, i18n } = useTranslation()
  const { user, login } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  // Already signed in (e.g. a returning visit to /login, or the moment sign-in resolves):
  // hand off to the declarative guard rather than showing the form again. ProtectedRoute then
  // takes it from here — a flagged user lands on /change-password automatically.
  if (user) {
    return <Navigate to="/" replace />
  }

  const toggleLanguage = () => {
    void i18n.changeLanguage(i18n.language.startsWith('ar') ? 'en' : 'ar')
  }

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
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--space-6)',
        background: 'var(--bg)',
      }}
    >
      {/* The toggle sits outside the card: the product is bilingual before you have an account,
          so the choice must be reachable on the very first screen. */}
      <div style={{ width: '100%', maxWidth: 400, display: 'flex', justifyContent: 'flex-end' }}>
        <button
          type="button"
          onClick={toggleLanguage}
          style={{
            height: 'var(--control-h-sm)',
            padding: '0 12px',
            marginBottom: 'var(--space-4)',
            border: '1px solid var(--border)',
            borderRadius: 'var(--r-md)',
            background: 'var(--surface)',
            color: 'var(--text-2)',
            fontFamily: 'inherit',
            fontSize: 13,
            cursor: 'pointer',
          }}
        >
          {i18n.language.startsWith('ar') ? t('language.en') : t('language.ar')}
        </button>
      </div>

      <form
        onSubmit={onSubmit}
        style={{
          width: '100%',
          maxWidth: 400,
          // The card is the whole screen on a phone, so its padding is the page gutter. Scales
          // with the viewport and stops at the designed --space-8.
          padding: 'clamp(var(--space-5), 6vw, var(--space-8))',
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 'var(--r-lg)',
          boxShadow: 'var(--shadow-med)',
          animation: 'fadeUp var(--motion-slow) var(--ease-standard)',
        }}
      >
        <div
          style={{
            width: 44,
            height: 44,
            borderRadius: 'var(--r-md)',
            background: 'var(--primary)',
            color: '#fff',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            marginBottom: 'var(--space-5)',
          }}
        >
          <FamilyIcon />
        </div>

        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700, color: 'var(--text-1)' }}>
          {t('auth.signIn')}
        </h1>
        <p style={{ margin: '6px 0 var(--space-6)', fontSize: 14, color: 'var(--text-3)' }}>
          {t('auth.subtitle')}
        </p>

        <div style={{ marginBottom: 'var(--space-4)' }}>
          <label htmlFor="email" style={labelStyle}>
            {t('auth.email')}
          </label>
          <input
            id="email"
            type="email"
            autoComplete="username"
            placeholder={t('auth.emailPlaceholder')}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            // Addresses are Latin even in the Arabic UI, so this field reads left-to-right.
            dir="ltr"
            style={{ ...fieldStyle, textAlign: 'start' }}
          />
        </div>

        <div style={{ marginBottom: 'var(--space-5)' }}>
          <label htmlFor="password" style={labelStyle}>
            {t('auth.password')}
          </label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            placeholder={t('auth.passwordPlaceholder')}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            style={fieldStyle}
          />
        </div>

        {errorCode !== null && (
          <p
            role="alert"
            style={{
              margin: '0 0 var(--space-5)',
              padding: '10px 12px',
              borderRadius: 'var(--r-md)',
              background: 'var(--error-subtle)',
              color: 'var(--error)',
              fontSize: 13,
            }}
          >
            {t(`errors.${errorCode}`, { defaultValue: t('errors.UNKNOWN') })}
          </p>
        )}

        <button
          type="submit"
          disabled={submitting}
          style={{
            width: '100%',
            height: 'var(--control-h-lg)',
            border: 'none',
            borderRadius: 'var(--r-md)',
            background: submitting ? 'var(--primary-active)' : 'var(--primary)',
            color: '#fff',
            fontFamily: 'inherit',
            fontSize: 14,
            fontWeight: 600,
            cursor: submitting ? 'progress' : 'pointer',
            transition: 'background var(--motion-fast) var(--ease-standard)',
          }}
        >
          {submitting ? t('auth.signingIn') : t('auth.signIn')}
        </button>
      </form>
    </div>
  )
}
