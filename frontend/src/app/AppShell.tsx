import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'
import { useIsCompact } from './useIsCompact'

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
  // Below the breakpoint the sidebar has nowhere to stand beside the canvas — 248 of a 320px
  // viewport — so the same <aside> is re-hung as an off-canvas drawer instead of shrunk.
  const isCompact = useIsCompact()
  const [drawerOpen, setDrawerOpen] = useState(false)

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

  // Arriving somewhere is the drawer completing its job, so navigation closes it. The second
  // effect covers the viewport growing back past the breakpoint while it is still flagged open.
  useEffect(() => setDrawerOpen(false), [pathname])
  useEffect(() => {
    if (!isCompact) setDrawerOpen(false)
  }, [isCompact])

  useEffect(() => {
    if (!drawerOpen) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setDrawerOpen(false)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [drawerOpen])

  const toggleLanguage = () => {
    void i18n.changeLanguage(i18n.language.startsWith('ar') ? 'en' : 'ar')
  }

  const sidebarVisible = !isCompact || drawerOpen

  const titleBlock = (
    <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, minWidth: 0 }}>
      <div
        style={{
          fontSize: isCompact ? 15 : 17,
          fontWeight: 600,
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {familyName}
      </div>
      {/* The name identifies the workspace and the stat line annotates it, so when the row runs
          short the annotation gives way first rather than both truncating at once. */}
      <div
        style={{
          fontSize: 12,
          color: 'var(--text-3)',
          whiteSpace: 'nowrap',
          flex: '0 1 auto',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {statLine}
      </div>
    </div>
  )

  const searchBox = (
    <div
      ref={searchRef}
      style={{
        position: 'relative',
        // On its own row when compact, so the field keeps its full width instead of competing
        // with the title, the language toggle and the avatar for one 60px row.
        marginInlineStart: isCompact ? undefined : 'auto',
        width: isCompact ? '100%' : 300,
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
            // Bounded by the viewport as well as by 320px, so the last hit stays reachable on
            // a short phone and on a laptop in landscape alike.
            maxHeight: 'min(320px, 60dvh)',
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
  )

  const languageButton = (
    <button
      type="button"
      onClick={toggleLanguage}
      style={{
        height: 36,
        flex: '0 0 auto',
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
  )

  const avatar = (
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
  )

  const menuButton = (
    <button
      type="button"
      onClick={() => setDrawerOpen((open) => !open)}
      aria-label={drawerOpen ? t('nav.closeMenu') : t('nav.openMenu')}
      aria-expanded={drawerOpen}
      style={{
        width: 40,
        height: 40,
        flex: '0 0 40px',
        border: '1px solid var(--border-strong)',
        borderRadius: 'var(--r-md)',
        background: 'var(--surface)',
        color: 'var(--text-2)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        cursor: 'pointer',
      }}
    >
      <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
        <path d="M4 7h16M4 12h16M4 17h16" />
      </svg>
    </button>
  )

  return (
    <div
      className="app-shell-root"
      style={{ display: 'flex', overflow: 'hidden', background: 'var(--bg)' }}
    >
      {/* One backdrop, one aside: the drawer is this navigation re-hung, not a second copy of
          it, so a destination added later cannot appear on only one of the two layouts. */}
      {isCompact && drawerOpen && (
        <div
          role="presentation"
          onClick={() => setDrawerOpen(false)}
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(21,24,27,.36)',
            zIndex: 300,
          }}
        />
      )}

      {sidebarVisible && (
        <aside
          style={
            isCompact
              ? {
                  // --z-drawer (300) is the backdrop; the panel sits one layer above it, and
                  // both stay under --z-modal (400) so a dialog still covers the drawer.
                  position: 'fixed',
                  insetBlock: 0,
                  insetInlineStart: 0,
                  width: 'min(280px, 85vw)',
                  zIndex: 301,
                  boxShadow: 'var(--shadow-high)',
                  background: 'var(--surface)',
                  borderInlineEnd: '1px solid var(--border)',
                  display: 'flex',
                  flexDirection: 'column',
                  padding: '18px 14px',
                  overflowY: 'auto',
                }
              : {
                  width: 248,
                  flex: '0 0 248px',
                  background: 'var(--surface)',
                  borderInlineEnd: '1px solid var(--border)',
                  display: 'flex',
                  flexDirection: 'column',
                  padding: '18px 14px',
                }
          }
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
            <div style={{ minWidth: 0, flex: 1 }}>
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
            {/* The drawer needs a way out that is not the backdrop: a control inside the panel
                is the only one a keyboard or screen reader reaches in reading order. */}
            {isCompact && (
              <button
                type="button"
                onClick={() => setDrawerOpen(false)}
                aria-label={t('nav.closeMenu')}
                style={{
                  width: 32,
                  height: 32,
                  flex: '0 0 32px',
                  border: 'none',
                  background: 'transparent',
                  color: 'var(--text-3)',
                  fontSize: 15,
                  cursor: 'pointer',
                  borderRadius: 'var(--r-sm)',
                }}
              >
                ✕
              </button>
            )}
          </div>

          {/* Closing on pathname alone misses the case of tapping the destination already open:
              the route never changes, so the drawer would stay up over the screen the user just
              asked to see. Handled here rather than on each item so a destination added later
              cannot forget it. On the wide layout there is no drawer and this is a no-op. */}
          <nav
            onClick={() => setDrawerOpen(false)}
            style={{ display: 'flex', flexDirection: 'column', gap: 2 }}
          >
            <Link to="/reports" style={navItemStyle(pathname === '/reports', true)}>
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
                <rect x="4" y="4" width="7" height="7" rx="1.5" />
                <rect x="13" y="4" width="7" height="7" rx="1.5" />
                <rect x="4" y="13" width="7" height="7" rx="1.5" />
                <rect x="13" y="13" width="7" height="7" rx="1.5" />
              </svg>
              {t('nav.reports')}
            </Link>
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
              <Link to="/users" style={navItemStyle(pathname === '/users', true)}>
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <circle cx="9" cy="8" r="3" />
                  <path d="M3 19c0-3.3 2.7-5 6-5s6 1.7 6 5M17 8h4M19 6v4" />
                </svg>
                {t('nav.users')}
              </Link>
            )}
            {hasPermission('Role.View') && (
              <Link to="/roles" style={navItemStyle(pathname === '/roles', true)}>
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M12 3l7 3v6c0 4-3 7.5-7 9-4-1.5-7-5-7-9V6z" />
                </svg>
                {t('nav.roles')}
              </Link>
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

          <div style={{ marginTop: 'auto', paddingTop: 22 }}>
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
      )}

      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <header
          style={
            isCompact
              ? {
                  flex: '0 0 auto',
                  background: 'var(--surface)',
                  borderBottom: '1px solid var(--border)',
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'stretch',
                  gap: 10,
                  padding: '10px 14px',
                }
              : {
                  height: 60,
                  flex: '0 0 60px',
                  background: 'var(--surface)',
                  borderBottom: '1px solid var(--border)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 14,
                  padding: '0 20px',
                }
          }
        >
          {isCompact ? (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                {menuButton}
                {titleBlock}
                <span style={{ marginInlineStart: 'auto' }} />
                {languageButton}
                {avatar}
              </div>
              {onQueryChange !== undefined && searchBox}
            </>
          ) : (
            <>
              {titleBlock}
              {searchBox}
              {languageButton}
              {avatar}
            </>
          )}
        </header>

        <div style={{ flex: 1, minHeight: 0, display: 'flex' }}>{children}</div>
      </div>
    </div>
  )
}
