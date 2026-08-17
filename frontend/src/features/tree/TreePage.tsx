import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AppShell, type SearchResult } from '../../app/AppShell'
import { useDirection } from '../../i18n/useDirection'
import { ApiError } from '../../services/apiClient'
import { useAuth } from '../auth/AuthContext'
import type { FamilyTreeNode } from '../members/types'
import {
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useTreeQuery,
  useUpdateMember,
} from '../members/useMembers'
import { ancestorIds, findNode, flattenTree, treeStats, type ExpandedMap } from './flattenTree'
import { ContextMenu, MemberModal, Toast, type MenuAnchor, type ModalKind } from './MemberActions'
import { MemberPanel } from './MemberPanel'
import { TreeCanvas } from './TreeCanvas'
import { useSearch } from './useSearch'

const ZOOM_STEP = 0.1
const ZOOM_MIN = 0.5
const ZOOM_MAX = 1.5
const TOAST_MS = 3200

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
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [panelOpen, setPanelOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [zoom, setZoom] = useState(1)
  const [revealId, setRevealId] = useState<string | null>(null)
  const [menu, setMenu] = useState<MenuAnchor | null>(null)
  const [modal, setModal] = useState<ModalKind | null>(null)
  const [nameValue, setNameValue] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [toast, setToast] = useState('')
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
  const rows = useMemo(
    () => (rootOpen ? flattenTree(roots, expanded, query) : []),
    [roots, expanded, query, rootOpen],
  )
  const stats = useMemo(() => treeStats(roots), [roots])
  const selected = selectedId === null ? undefined : findNode(roots, selectedId)

  const detailById = useMemo(() => {
    const map = new Map<string, { version: number; createdAt: string; updatedAt: string }>()
    ;(members ?? []).forEach((m) =>
      map.set(m.id, { version: m.version, createdAt: m.createdAt, updatedAt: m.updatedAt }),
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

  const openModal = (kind: ModalKind, initialName = '') => {
    setModal(kind)
    setNameValue(initialName)
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
        { name: nameValue, parentId },
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
        { id: selected.id, name: nameValue, version: detail.version },
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
          onEdit={() => openModal('edit', selected?.name ?? '')}
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
          errorCode={errorCode}
          isSaving={createMember.isPending || updateMember.isPending || deleteMember.isPending}
          onNameChange={setNameValue}
          onCancel={closeModal}
          onConfirm={confirm}
        />
      )}

      {toast !== '' && <Toast message={toast} />}
    </AppShell>
  )
}
