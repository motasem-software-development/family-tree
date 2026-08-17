import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'

const FamilyIcon = ({ size = 17 }: { size?: number }) => (
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

const navItemStyle = (active: boolean, enabled: boolean): CSSProperties => ({
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  width: '100%',
  padding: '9px 10px',
  border: 'none',
  borderRadius: 'var(--r-md)',
  background: active ? 'var(--primary-subtle)' : 'transparent',
  color: active ? 'var(--primary-hover)' : enabled ? 'var(--text-2)' : 'var(--text-disabled)',
  fontWeight: active ? 600 : 400,
  fontSize: 14,
  fontFamily: 'inherit',
  textAlign: 'start',
  cursor: enabled ? 'pointer' : 'not-allowed',
  textDecoration: 'none',
})

/** A nav destination whose phase has not shipped. Visible, but honestly inert. */
const PendingNavItem = ({ label, icon }: { label: string; icon: ReactNode }) => {
  const { t } = useTranslation()
  return (
    <button type="button" disabled title={t('nav.comingSoon')} style={navItemStyle(false, false)}>
      {icon}
      {label}
    </button>
  )
}

/**
 * A search hit as the shell sees it: an id to select, a name to show, and a caption that
 * disambiguates. The shell no longer takes a FamilyTreeNode — results come from the server and
 * may name members that are not in the loaded tree at all.
 */
export interface SearchResult {
  id: string
  name: string
  meta: string
}

interface AppShellProps {
  familyName: string
  statLine: string
  /** Omit the search props on screens that have nothing to search — the box is then hidden. */
  query?: string
  results?: readonly SearchResult[]
  /** Every server-side match, which may exceed `results.length`. */
  resultTotal?: number
  isSearching?: boolean
  /** The user has typed something, but not enough characters to search yet. */
  belowThreshold?: boolean
  onQueryChange?: (value: string) => void
  onSelectResult?: (id: string) => void
  children: ReactNode
}

export const AppShell = ({
  familyName,
  statLine,
  query = '',
  results = [],
  resultTotal = 0,
  isSearching = false,
  belowThreshold = false,
  onQueryChange,
  onSelectResult,
  children,
}: AppShellProps) => {
  const { t, i18n } = useTranslation()
  const { user, hasPermission, logout } = useAuth()
  const { pathname } = useLocation()

  const initials = (user?.email ?? '?').slice(0, 2).toUpperCase()

  // The popover is dismissible on its own, separately from the query: Escape cancels the
  // search outright, while clicking away only puts the list down — the matches stay
  // highlighted on the canvas, which is the point of a search that dims instead of filters.
  const searchRef = useRef<HTMLDivElement>(null)
  const [dismissed, setDismissed] = useState(false)
  const showResults = query.trim().length > 0 && !dismissed

  useEffect(() => {
    if (!showResults) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      // Only while focus is inside the search box. Escape belongs to the topmost layer, and
      // a document-wide handler here would swallow the press that should close an open modal.
      if (!searchRef.current?.contains(document.activeElement)) return
      onQueryChange?.('')
      setDismissed(false)
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!searchRef.current?.contains(event.target as Node)) setDismissed(true)
    }

    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPointerDown)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointerDown)
    }
  }, [showResults, onQueryChange])

  const toggleLanguage = () => {
    void i18n.changeLanguage(i18n.language.startsWith('ar') ? 'en' : 'ar')
  }

  return (
    <div style={{ display: 'flex', height: '100vh', overflow: 'hidden', background: 'var(--bg)' }}>
      <aside
        style={{
          width: 248,
          flex: '0 0 248px',
          background: 'var(--surface)',
          borderInlineEnd: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          padding: '18px 14px',
        }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '0 8px',
            marginBottom: 22,
          }}
        >
          <div
            style={{
              width: 28,
              height: 28,
              borderRadius: 'var(--r-sm)',
              background: 'var(--primary)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#fff',
              flex: '0 0 28px',
            }}
          >
            <FamilyIcon />
          </div>
          <div style={{ minWidth: 0 }}>
            <div
              style={{
                fontSize: 14,
                fontWeight: 600,
                lineHeight: 1.3,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {familyName}
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-3)' }}>{t('nav.tenantTag')}</div>
          </div>
        </div>

        <nav style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <PendingNavItem
            label={t('nav.dashboard')}
            icon={
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
                <rect x="4" y="4" width="7" height="7" rx="1.5" />
                <rect x="13" y="4" width="7" height="7" rx="1.5" />
                <rect x="4" y="13" width="7" height="7" rx="1.5" />
                <rect x="13" y="13" width="7" height="7" rx="1.5" />
              </svg>
            }
          />
          <Link to="/" style={navItemStyle(pathname === '/', true)}>
            <FamilyIcon />
            {t('nav.tree')}
          </Link>
          <Link to="/members" style={navItemStyle(pathname === '/members', true)}>
            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
              <path d="M5 6h14M5 12h14M5 18h14" />
            </svg>
            {t('nav.members')}
          </Link>
          {hasPermission('User.View') && (
            <PendingNavItem
              label={t('nav.users')}
              icon={
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <circle cx="9" cy="8" r="3" />
                  <path d="M3 19c0-3.3 2.7-5 6-5s6 1.7 6 5M17 8h4M19 6v4" />
                </svg>
              }
            />
          )}
          {hasPermission('Role.View') && (
            <PendingNavItem
              label={t('nav.roles')}
              icon={
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M12 3l7 3v6c0 4-3 7.5-7 9-4-1.5-7-5-7-9V6z" />
                </svg>
              }
            />
          )}
          {hasPermission('Audit.View') && (
            <PendingNavItem
              label={t('nav.audit')}
              icon={
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
                  <circle cx="12" cy="12" r="8" />
                  <path d="M12 7.5V12l3 2" />
                </svg>
              }
            />
          )}
          {hasPermission('PublicLink.Create') && (
            <PendingNavItem
              label={t('nav.share')}
              icon={
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <circle cx="17" cy="6" r="2.5" />
                  <circle cx="7" cy="12" r="2.5" />
                  <circle cx="17" cy="18" r="2.5" />
                  <path d="M9.2 10.8 14.8 7.2M9.2 13.2l5.6 3.6" />
                </svg>
              }
            />
          )}
          <div style={{ height: 1, background: 'var(--divider)', margin: '8px 4px' }} />
          <PendingNavItem
            label={t('nav.settings')}
            icon={
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
                <circle cx="12" cy="12" r="3" />
                <path d="M12 3v3M12 18v3M3 12h3M18 12h3" />
              </svg>
            }
          />
        </nav>

        <div style={{ marginTop: 'auto' }}>
          <button
            type="button"
            onClick={() => void logout()}
            style={{
              ...navItemStyle(false, true),
              border: '1px solid var(--border)',
              background: 'var(--surface)',
            }}
          >
            {t('auth.signOut')}
          </button>
        </div>
      </aside>

      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <header
          style={{
            height: 60,
            flex: '0 0 60px',
            background: 'var(--surface)',
            borderBottom: '1px solid var(--border)',
            display: 'flex',
            alignItems: 'center',
            gap: 14,
            padding: '0 20px',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, minWidth: 0 }}>
            <div style={{ fontSize: 17, fontWeight: 600, whiteSpace: 'nowrap' }}>{familyName}</div>
            <div style={{ fontSize: 12, color: 'var(--text-3)', whiteSpace: 'nowrap' }}>
              {statLine}
            </div>
          </div>

          <div
            ref={searchRef}
            style={{
              position: 'relative',
              marginInlineStart: 'auto',
              width: 300,
              display: onQueryChange === undefined ? 'none' : 'block',
            }}
          >
            <div
              style={{
                height: 36,
                border: '1px solid var(--border-strong)',
                borderRadius: 'var(--r-md)',
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                padding: '0 10px',
                background: 'var(--surface)',
              }}
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#79838B" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
                <circle cx="11" cy="11" r="6" />
                <path d="M15.5 15.5 20 20" />
              </svg>
              <input
                value={query}
                onChange={(event) => {
                  setDismissed(false)
                  onQueryChange?.(event.target.value)
                }}
                onFocus={() => setDismissed(false)}
                placeholder={t('tree.searchPlaceholder')}
                aria-label={t('tree.searchPlaceholder')}
                style={{
                  flex: 1,
                  minWidth: 0,
                  border: 'none',
                  outline: 'none',
                  background: 'transparent',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  color: 'var(--text-1)',
                }}
              />
            </div>
            {showResults && (
              <div
                style={{
                  position: 'absolute',
                  top: 42,
                  insetInlineStart: 0,
                  width: '100%',
                  background: 'var(--surface)',
                  border: '1px solid var(--border)',
                  borderRadius: 'var(--r-md)',
                  boxShadow: 'var(--shadow-med)',
                  overflow: 'hidden',
                  zIndex: 40,
                  maxHeight: 320,
                  overflowY: 'auto',
                }}
              >
                {/* A count is a claim about a completed search: withhold it while one is
                    pending (isSearching) or impossible (belowThreshold). */}
                {!isSearching && !belowThreshold && (
                  <div
                    style={{
                      padding: '8px 12px',
                      fontSize: 11,
                      color: 'var(--text-3)',
                      borderBottom: '1px solid var(--divider)',
                    }}
                  >
                    {results.length < resultTotal
                      ? t('tree.resultCountPartial', { shown: results.length, total: resultTotal })
                      : t('tree.resultCount', { count: resultTotal })}
                  </div>
                )}
                {results.map((result) => (
                  <button
                    key={result.id}
                    type="button"
                    onClick={() => onSelectResult?.(result.id)}
                    style={{
                      display: 'block',
                      width: '100%',
                      textAlign: 'start',
                      padding: '9px 12px',
                      border: 'none',
                      borderBottom: '1px solid var(--divider)',
                      background: 'transparent',
                      fontFamily: 'inherit',
                      cursor: 'pointer',
                    }}
                  >
                    <div style={{ fontSize: 13, fontWeight: 500 }}>{result.name}</div>
                    <div style={{ fontSize: 11, color: 'var(--text-3)', marginTop: 2 }}>
                      {result.meta}
                    </div>
                  </button>
                ))}
                {results.length === 0 && (
                  <div
                    style={{
                      padding: '18px 12px',
                      textAlign: 'center',
                      fontSize: 13,
                      color: 'var(--text-2)',
                    }}
                  >
                    {/* "No matches" is a claim about the data; while a request is in flight, or
                        below the minimum query length, it is not yet true — belowThreshold
                        takes precedence since no request can be in flight for it. */}
                    {belowThreshold
                      ? t('tree.keepTyping')
                      : isSearching
                        ? t('tree.searching')
                        : t('tree.noResults')}
                  </div>
                )}
              </div>
            )}
          </div>

          <button
            type="button"
            onClick={toggleLanguage}
            style={{
              height: 36,
              padding: '0 12px',
              border: '1px solid var(--border-strong)',
              borderRadius: 'var(--r-md)',
              background: 'var(--surface)',
              fontFamily: 'inherit',
              fontSize: 13,
              cursor: 'pointer',
            }}
          >
            {i18n.language.startsWith('ar') ? t('language.en') : t('language.ar')}
          </button>

          <span
            title={user?.email ?? ''}
            style={{
              width: 32,
              height: 32,
              borderRadius: '50%',
              background: 'var(--primary-subtle)',
              color: 'var(--primary-hover)',
              fontSize: 12,
              fontWeight: 600,
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              flex: '0 0 32px',
            }}
          >
            {initials}
          </span>
        </header>

        <div style={{ flex: 1, minHeight: 0, display: 'flex' }}>{children}</div>
      </div>
    </div>
  )
}
