import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate } from 'react-router-dom'
import { ApiError, apiFetch } from '../../services/apiClient'
import { useAuth } from './AuthContext'

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

export const ChangePasswordPage = () => {
  const { t } = useTranslation()
  const { user, login, logout, mustChangePassword } = useAuth()

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [mismatch, setMismatch] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  // The change itself succeeded but the re-login did not, so the user must sign in by hand.
  // Tracked separately from errorCode because the two say opposite things about whether the
  // password changed, and reporting the login failure as though the change had failed is the
  // defect this exists to prevent.
  const [changedNeedsSignIn, setChangedNeedsSignIn] = useState(false)
  const [submitting, setSubmitting] = useState(false)

  // A user only ever lands here with the flag set (ProtectedRoute sends them here precisely
  // because it is true), so this cannot fire before the change actually completes: it only
  // becomes true once the re-login below invalidates ['me'] and the refetch comes back clear.
  if (user && !mustChangePassword) {
    return <Navigate to="/" replace />
  }

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setMismatch(false)
    setErrorCode(null)

    if (newPassword !== confirmPassword) {
      setMismatch(true)
      return
    }

    setSubmitting(true)

    // Two steps, two catches. Everything after this point has already changed the password,
    // so a failure there can never be reported as "the change failed".
    try {
      await apiFetch('/api/v1/me/password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      })
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'NETWORK')
      setSubmitting(false)
      return
    }

    // The server has cleared the DB flag and revoked every refresh token this session held, so
    // the access token still in hand carries the stale must_change_password claim and there is
    // no unrevoked refresh token to trade for a fresh one. Signing in again through the normal
    // login path mints a token pair with no stale claim and invalidates the `['me']` query,
    // which is what actually releases the redirect above — the redirect itself is UX only; the
    // server gate (PasswordChangeGateMiddleware) is the real enforcement point (§9).
    //
    // If that fails — transiently, or because `user` is null and there is no email to sign in
    // with — the only honest thing to say is that the password DID change and the user must
    // sign in again. Saying so beats calling logout(): a silent bounce to /login with no
    // explanation looks exactly like the failure this screen is trying to report. The submit
    // button is disabled from here on, because retrying would send a "current password" the
    // server no longer recognises and answer PASSWORD_INCORRECT.
    try {
      if (!user) throw new ApiError('NO_SESSION', 0)
      await login(user.email, newPassword)
    } catch {
      setChangedNeedsSignIn(true)
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
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700, color: 'var(--text-1)' }}>
          {t('auth.changePasswordTitle')}
        </h1>
        <p style={{ margin: '6px 0 var(--space-6)', fontSize: 14, color: 'var(--text-3)' }}>
          {t('auth.changePasswordSubtitle')}
        </p>

        <div style={{ marginBottom: 'var(--space-4)' }}>
          <label htmlFor="currentPassword" style={labelStyle}>
            {t('auth.currentPassword')}
          </label>
          <input
            id="currentPassword"
            type="password"
            autoComplete="current-password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            required
            style={fieldStyle}
          />
        </div>

        <div style={{ marginBottom: 'var(--space-4)' }}>
          <label htmlFor="newPassword" style={labelStyle}>
            {t('auth.newPassword')}
          </label>
          <input
            id="newPassword"
            type="password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            required
            style={fieldStyle}
          />
        </div>

        <div style={{ marginBottom: 'var(--space-5)' }}>
          <label htmlFor="confirmPassword" style={labelStyle}>
            {t('auth.confirmPassword')}
          </label>
          <input
            id="confirmPassword"
            type="password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            style={fieldStyle}
          />
        </div>

        {mismatch && (
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
            {t('auth.passwordMismatch')}
          </p>
        )}

        {changedNeedsSignIn && (
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
            {t('auth.passwordChangedSignInAgain')}
          </p>
        )}

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
          disabled={submitting || changedNeedsSignIn}
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
          {submitting ? t('auth.changingPassword') : t('auth.changePassword')}
        </button>

        {/* The server deliberately keeps this route reachable without a valid session claim
            (PasswordChangeGateMiddleware's IAllowAnonymous carve-out) so a user who cannot
            recall the temporary password an administrator gave them is never stranded here. */}
        <button
          type="button"
          onClick={() => void logout()}
          style={{
            width: '100%',
            height: 'var(--control-h-sm)',
            marginTop: 'var(--space-3)',
            border: '1px solid var(--border)',
            borderRadius: 'var(--r-md)',
            background: 'var(--surface)',
            color: 'var(--text-2)',
            fontFamily: 'inherit',
            fontSize: 13,
            cursor: 'pointer',
          }}
        >
          {t('auth.signOut')}
        </button>
      </form>
    </div>
  )
}
