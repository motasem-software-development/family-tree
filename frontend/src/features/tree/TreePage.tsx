import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { AppShell, type SearchResult } from '../../app/AppShell'
import { useDirection } from '../../i18n/useDirection'
import { ApiError } from '../../services/apiClient'
import { useAuth } from '../auth/AuthContext'
import {
  EMPTY_LIFE_DETAILS,
  lifeDetailsOf,
  type LifeDetails,
} from '../members/lifeDetails'
import type { FamilyTreeNode } from '../members/types'
import {
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useTreeQuery,
  useUpdateMember,
} from '../members/useMembers'
import { ExportDialog } from './ExportDialog'
import { ancestorIds, findNode, flattenTree, treeStats, type ExpandedMap } from './flattenTree'
import { ContextMenu, MemberModal, Toast, type MenuAnchor, type ModalKind } from './MemberActions'
import { MemberPanel } from './MemberPanel'
import { TreeCanvas } from './TreeCanvas'
import { useSearch } from './useSearch'

const ZOOM_STEP = 0.1
const ZOOM_MIN = 0.5
const ZOOM_MAX = 1.5
const TOAST_MS = 3200
// MemberPanel's own width (see MemberPanel.tsx's `aside`) plus a clear gap. The export button
// is a fixed overlay — TreePage has no in-flow toolbar of its own to host it, and the two
// components that do (AppShell's header, shared by every screen, and TreeCanvas's floating
// zoom toolbar) are outside this change's scope. Shifting clear of the panel's box keeps the
// overlap structurally impossible instead of merely unlikely.
const MEMBER_PANEL_WIDTH = 320
const EXPORT_BUTTON_GAP = 24

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

export const TreePage = () => {
  const { t } = useTranslation()
  const direction = useDirection()
  const { hasPermission } = useAuth()

  const { data: view, isLoading } = useTreeQuery()
  // The tree endpoint returns structure only. The flat list carries `version` (needed for every
  // rename) plus createdAt/updatedAt for the detail panel, so both load and join by id.
  const { data: members } = useMembersQuery()

  const [expanded, setExpanded] = useState<ExpandedMap>({})
  const [rootOpen, setRootOpen] = useState(true)
  const [searchParams] = useSearchParams()
  // Seeded from the URL so a report row can link straight to a member (design §8). A lazy
  // initialiser, not an effect: the parameter is the starting selection, not a binding — a
  // later click must be free to select something else without the URL fighting it back.
  const [selectedId, setSelectedId] = useState<string | null>(() => searchParams.get('memberId'))
  const [panelOpen, setPanelOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [zoom, setZoom] = useState(1)
  const [revealId, setRevealId] = useState<string | null>(null)
  const [menu, setMenu] = useState<MenuAnchor | null>(null)
  const [modal, setModal] = useState<ModalKind | null>(null)
  const [nameValue, setNameValue] = useState('')
  const [lifeValue, setLifeValue] = useState<LifeDetails>(EMPTY_LIFE_DETAILS)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [toast, setToast] = useState('')
  const [exportOpen, setExportOpen] = useState(false)
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()

  useEffect(
    () => () => {
      if (toastTimer.current !== null) clearTimeout(toastTimer.current)
    },
    [],
  )

  const showToast = useCallback((message: string) => {
    if (toastTimer.current !== null) clearTimeout(toastTimer.current)
    setToast(message)
    toastTimer.current = setTimeout(() => setToast(''), TOAST_MS)
  }, [])

  const roots = useMemo(() => view?.rootMembers ?? [], [view])
  // Life details for the outline, keyed by id. Separate from detailById below only because the
  // rows are built before it — same join, same source, no second fetch.
  const lifeById = useMemo(
    () => new Map((members ?? []).map((m) => [m.id, lifeDetailsOf(m)])),
    [members],
  )
  const rows = useMemo(
    () => (rootOpen ? flattenTree(roots, expanded, query, lifeById) : []),
    [roots, expanded, query, rootOpen, lifeById],
  )
  const stats = useMemo(() => treeStats(roots), [roots])
  const selected = selectedId === null ? undefined : findNode(roots, selectedId)

  const detailById = useMemo(() => {
    const map = new Map<
      string,
      { version: number; createdAt: string; updatedAt: string; life: LifeDetails }
    >()
    ;(members ?? []).forEach((m) =>
      map.set(m.id, {
        version: m.version,
        createdAt: m.createdAt,
        updatedAt: m.updatedAt,
        life: lifeDetailsOf(m),
      }),
    )
    return map
  }, [members])

  const { page: searchPage, isSearching, belowThreshold } = useSearch(query)

  const results = useMemo<SearchResult[]>(
    () =>
      searchPage.items.map((hit) => ({
        id: hit.id,
        name: hit.name,
        // The path is the whole point: 39 members are named محمد, and their ancestry is the
        // only thing that tells them apart (design spec §5.4). A root member has no path, so
        // fall back to the generation rather than showing an empty caption.
        meta:
          hit.ancestors.length > 0
            ? hit.ancestors.map((ancestor) => ancestor.name).join(' ‹ ')
            : `${t('tree.gen')} ${hit.generation}`,
      })),
    [searchPage, t],
  )

  const permissions = {
    canCreate: hasPermission('Member.Create'),
    canEdit: hasPermission('Member.Edit'),
    canDelete: hasPermission('Member.Delete'),
  }

  const familyName = view?.name ?? ''
  const statLine = `${t('tree.membersCount', { count: stats.members })} · ${t('tree.generationsCount', { count: stats.generations })}`

  const toggle = (id: string) =>
    setExpanded((current) => ({ ...current, [id]: current[id] !== true }))

  const select = (id: string) => {
    setSelectedId(id)
    setPanelOpen(true)
  }

  const clearReveal = useCallback(() => setRevealId(null), [])

  /** Reveal a search hit: open every branch above it, scroll to it, then select it. */
  const revealResult = (id: string) => {
    const opened: Record<string, boolean> = { ...expanded }
    ancestorIds(roots, id).forEach((ancestor) => {
      opened[ancestor] = true
    })
    setRootOpen(true)
    setExpanded(opened)
    setSelectedId(id)
    setPanelOpen(true)
    // Expanding ancestors used to be enough — the row was always in the DOM. Windowed, it may
    // not be, so the canvas is asked to scroll to it once this render settles.
    setRevealId(id)
    setQuery('')
  }

  const openModal = (kind: ModalKind, initialName = '', initialLife = EMPTY_LIFE_DETAILS) => {
    setModal(kind)
    setNameValue(initialName)
    setLifeValue(initialLife)
    setErrorCode(null)
    setMenu(null)
  }

  const openDelete = (node: FamilyTreeNode) => {
    // The server is the authority — this only picks the right dialog up front. A concurrent
    // insert still comes back as 409 MEMBER_HAS_CHILDREN, handled in confirm().
    openModal(node.children.length > 0 ? 'blocked' : 'delete')
  }

  const closeModal = () => {
    setModal(null)
    setErrorCode(null)
  }

  const confirm = () => {
    if (modal === 'add') {
      setErrorCode(null)
      const parentId = selectedId
      createMember.mutate(
        { name: nameValue, parentId, life: lifeValue },
        {
          onSuccess: (created) => {
            if (parentId !== null) setExpanded((current) => ({ ...current, [parentId]: true }))
            setSelectedId(created.id)
            setPanelOpen(true)
            closeModal()
            showToast(t('toast.added', { name: created.name }))
          },
          onError: (error) => setErrorCode(codeOf(error)),
        },
      )
      return
    }

    if (modal === 'edit' && selected !== undefined) {
      const detail = detailById.get(selected.id)
      if (detail === undefined) {
        setErrorCode('MEMBER_NOT_FOUND')
        return
      }
      setErrorCode(null)
      updateMember.mutate(
        { id: selected.id, name: nameValue, version: detail.version, life: lifeValue },
        {
          onSuccess: () => {
            closeModal()
            showToast(t('toast.saved'))
          },
          onError: (error) => setErrorCode(codeOf(error)),
        },
      )
      return
    }

    if (modal === 'delete' && selected !== undefined) {
      const name = selected.name
      setErrorCode(null)
      deleteMember.mutate(selected.id, {
        onSuccess: () => {
          setSelectedId(null)
          setPanelOpen(false)
          closeModal()
          showToast(t('toast.deleted', { name }))
        },
        onError: (error) => {
          const code = codeOf(error)
          // A child appeared between opening the dialog and confirming: show the blocked
          // dialog rather than an error under a confirm button that can never succeed.
          if (code === 'MEMBER_HAS_CHILDREN') setModal('blocked')
          else setErrorCode(code)
        },
      })
    }
  }

  const parentNameOf = (node: FamilyTreeNode | undefined): string => {
    if (node === undefined || node.parentId === null) return familyName
    return findNode(roots, node.parentId)?.name ?? familyName
  }

  const detail = selected === undefined ? undefined : detailById.get(selected.id)

  return (
    <AppShell
      familyName={familyName}
      statLine={statLine}
      query={query}
      results={results}
      resultTotal={searchPage.total}
      isSearching={isSearching}
      belowThreshold={belowThreshold}
      onQueryChange={setQuery}
      onSelectResult={revealResult}
    >
      <TreeCanvas
        familyName={familyName}
        rootCount={roots.length}
        rootOpen={rootOpen}
        rows={rows}
        selectedId={selectedId}
        direction={direction}
        zoom={zoom}
        isLoading={isLoading}
        revealId={revealId}
        onRevealed={clearReveal}
        onToggleRoot={() => setRootOpen((open) => !open)}
        onToggle={toggle}
        onSelect={select}
        onMenu={(id, anchor) => {
          setSelectedId(id)
          setMenu({
            id,
            top: anchor.bottom + 6,
            inlineStart: direction === 'rtl' ? window.innerWidth - anchor.right : anchor.left,
          })
        }}
        onZoomIn={() => setZoom((z) => Math.min(ZOOM_MAX, z + ZOOM_STEP))}
        onZoomOut={() => setZoom((z) => Math.max(ZOOM_MIN, z - ZOOM_STEP))}
        onZoomReset={() => setZoom(1)}
        onCollapseAll={() => setExpanded({})}
        onAddFirst={() => {
          setSelectedId(null)
          openModal('add')
        }}
      />

      {panelOpen && selected !== undefined && (
        <MemberPanel
          member={selected}
          parentName={parentNameOf(selected)}
          createdAt={detail?.createdAt}
          updatedAt={detail?.updatedAt}
          life={detail?.life}
          permissions={permissions}
          onClose={() => {
            setPanelOpen(false)
            setSelectedId(null)
          }}
          onAdd={() => openModal('add')}
          onEdit={() => openModal('edit', selected.name)}
          onDelete={() => openDelete(selected)}
        />
      )}

      {menu !== null && (
        <ContextMenu
          anchor={menu}
          permissions={permissions}
          onClose={() => setMenu(null)}
          onViewDetails={() => {
            setPanelOpen(true)
            setMenu(null)
          }}
          onAdd={() => openModal('add')}
          onEdit={() =>
            openModal(
              'edit',
              selected?.name ?? '',
              // Seeded from the flat list, the same join the panel reads: opening the editor
              // must show the dates already on file, not blank fields that would clear them.
              selectedId === null
                ? EMPTY_LIFE_DETAILS
                : (detailById.get(selectedId)?.life ?? EMPTY_LIFE_DETAILS),
            )
          }
          onDelete={() => {
            if (selected !== undefined) openDelete(selected)
          }}
        />
      )}

      {modal !== null && (
        <MemberModal
          kind={modal}
          subjectName={selected?.name ?? familyName}
          parentName={modal === 'add' ? (selected?.name ?? familyName) : parentNameOf(selected)}
          childNames={selected?.children.map((child) => child.name) ?? []}
          nameValue={nameValue}
          lifeValue={lifeValue}
          errorCode={errorCode}
          isSaving={createMember.isPending || updateMember.isPending || deleteMember.isPending}
          onNameChange={setNameValue}
          onLifeChange={setLifeValue}
          onCancel={closeModal}
          onConfirm={confirm}
        />
      )}

      <button
        type="button"
        onClick={() => setExportOpen(true)}
        style={{
          position: 'fixed',
          top: 76,
          // Cleared past MemberPanel's own width when it's on screen, so the two boxes never
          // share inline-axis space — see the MEMBER_PANEL_WIDTH comment above.
          insetInlineEnd:
            panelOpen && selected !== undefined
              ? MEMBER_PANEL_WIDTH + EXPORT_BUTTON_GAP
              : EXPORT_BUTTON_GAP,
          height: 36,
          padding: '0 14px',
          border: '1px solid var(--border-strong)',
          borderRadius: 'var(--r-md)',
          background: 'var(--surface)',
          fontFamily: 'inherit',
          fontSize: 13,
          fontWeight: 500,
          cursor: 'pointer',
          boxShadow: 'var(--shadow-med)',
          zIndex: 30,
        }}
      >
        {t('tree.export.button')}
      </button>

      {exportOpen && (
        // Whole-tree export is the unsurprising default: the dialog gives no cue about which
        // member is "selected", so scoping silently to it would produce a truncated PDF with
        // no explanation. ExportDialog still accepts a rootId for whoever wires subtree export
        // to an explicit affordance later.
        <ExportDialog
          fileName={`${familyName || 'family-tree'}.pdf`}
          onClose={() => setExportOpen(false)}
        />
      )}

      {toast !== '' && <Toast message={toast} />}
    </AppShell>
  )
}
