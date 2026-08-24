import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { AppShell, type SearchResult } from '../../app/AppShell'
import { useDirection } from '../../i18n/useDirection'
import { ApiError } from '../../services/apiClient'
import { useAuth } from '../auth/AuthContext'
import { useCountriesQuery } from '../countries/useCountries'
import { FilterControls } from '../filters/FilterControls'
import { useMemberFilters } from '../filters/useMemberFilters'
import { EMPTY_CONTACT_DETAILS, contactDetailsOf, type ContactDetails } from '../members/contactDetails'
import {
  EMPTY_LIFE_DETAILS,
  lifeDetailsOf,
  type LifeDetails,
} from '../members/lifeDetails'
import { NAME_PART_COUNT, fullName, indexById, lineageName, nameParts } from '../members/fullName'
import type { FamilyTreeNode } from '../members/types'
import {
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useMoveMember,
  useTreeQuery,
  useUpdateMember,
} from '../members/useMembers'
import { ExportDialog } from './ExportDialog'
import { rootGenerationOf, rootRelative } from './generation'
import {
  allNodes,
  ancestorIds,
  descendantIds,
  findNode,
  flattenTree,
  treeStats,
  type ExpandedMap,
} from './flattenTree'
import { ContextMenu, MemberModal, Toast, type MenuAnchor, type ModalKind } from './MemberActions'
import { MemberPanel } from './MemberPanel'
import { MoveDialog } from './MoveDialog'
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

  const { filters, activeCount, setFilter, reset } = useMemberFilters()
  const { data: view, isLoading } = useTreeQuery(filters)
  // The tree endpoint returns structure only. The flat list carries `version` (needed for every
  // rename) plus createdAt/updatedAt for the detail panel, so both load and join by id.
  //
  // Unfiltered on purpose: it is a lookup table, not a view. A member kept visible by the
  // ancestor rule still needs their version to be renamable, and a filtered list would leave
  // them without one.
  const { data: members } = useMembersQuery()
  const { data: countries } = useCountriesQuery()

  const [expanded, setExpanded] = useState<ExpandedMap>({})
  const [rootOpen, setRootOpen] = useState(true)
  const [searchParams] = useSearchParams()
  // The id a report row linked to, captured once at mount. A ref, not state: it is consulted
  // exactly once (by the reveal effect below, once the tree has loaded) and then cleared, so it
  // can never re-apply itself over a selection the user makes afterward.
  const initialMemberId = useRef(searchParams.get('memberId'))
  // Seeded from the URL so a report row can land on a highlighted, open panel immediately,
  // before the tree data (and therefore the member's ancestor chain) has even loaded. Lazy
  // initialisers, not effects: this is the starting selection, not a binding — a later click
  // must be free to select something else without the URL fighting it back. If the id turns out
  // to match nothing, the reveal effect below clears both once the data confirms it.
  const [selectedId, setSelectedId] = useState<string | null>(() => initialMemberId.current)
  const [panelOpen, setPanelOpen] = useState<boolean>(() => initialMemberId.current !== null)
  const [query, setQuery] = useState('')
  const [zoom, setZoom] = useState(1)
  const [revealId, setRevealId] = useState<string | null>(null)
  const [menu, setMenu] = useState<MenuAnchor | null>(null)
  const [modal, setModal] = useState<ModalKind | null>(null)
  const [nameValue, setNameValue] = useState('')
  const [lifeValue, setLifeValue] = useState<LifeDetails>(EMPTY_LIFE_DETAILS)
  const [contactValue, setContactValue] = useState<ContactDetails>(EMPTY_CONTACT_DETAILS)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [toast, setToast] = useState('')
  const [exportOpen, setExportOpen] = useState(false)
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()
  const moveMember = useMoveMember()
  const [moveOpen, setMoveOpen] = useState(false)

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
  /**
   * A filtered tree arrives already pruned to the matches and the ancestors holding them up, so
   * every branch still in it is one the user asked to see — it is opened for them.
   *
   * Without this a filter looked broken: the outline starts collapsed and nothing else expands
   * it, so applying one left a single dimmed, unclickable root row and the matches had to be
   * hand-expanded generation by generation. The unfiltered tree stays collapsed, because opening
   * all 351 members would bury the user.
   */
  const effectiveExpanded = useMemo<ExpandedMap>(
    () =>
      activeCount === 0
        ? expanded
        : Object.fromEntries(allNodes(roots).map((node) => [node.id, expanded[node.id] !== false])),
    [activeCount, roots, expanded],
  )

  const rows = useMemo(
    () => (rootOpen ? flattenTree(roots, effectiveExpanded, query, lifeById) : []),
    [roots, effectiveExpanded, query, rootOpen, lifeById],
  )
  const stats = useMemo(() => treeStats(roots), [roots])
  // The offset both display sites measure against (design spec §1.2). Derived from the view, so
  // it stays right if a root is ever selected — subtracting one would not.
  const rootGeneration = rootGenerationOf(roots)
  const selected = selectedId === null ? undefined : findNode(roots, selectedId)
  // Every ancestor of a selectable node is loaded by construction — a row can only be clicked
  // once its branch is expanded — so the chain composes off the tree without a second fetch.
  const byId = useMemo(() => indexById(allNodes(roots)), [roots])

  const detailById = useMemo(() => {
    const map = new Map<
      string,
      {
        version: number
        createdAt: string
        updatedAt: string
        life: LifeDetails
        contact: ContactDetails
      }
    >()
    ;(members ?? []).forEach((m) =>
      map.set(m.id, {
        version: m.version,
        createdAt: m.createdAt,
        updatedAt: m.updatedAt,
        life: lifeDetailsOf(m),
        contact: contactDetailsOf(m),
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
            : // Root-relative, like the panel and the filter: the search endpoint's generation
              // is absolute 1-based, and two captions on one page must not disagree.
              `${t('tree.gen')} ${rootRelative(hit.generation, rootGeneration)}`,
      })),
    [searchPage, t, rootGeneration],
  )

  const permissions = {
    canCreate: hasPermission('Member.Create'),
    canEdit: hasPermission('Member.Edit'),
    canDelete: hasPermission('Member.Delete'),
    canMove: hasPermission('Member.Move'),
  }

  const familyName = view?.name ?? ''
  const statLine = `${t('tree.membersCount', { count: stats.members })} · ${t('tree.generationsCount', { count: stats.generations })}`

  const toggle = (id: string) => {
    // Against what the row is actually showing, not the raw map: under a filter a row with no
    // entry of its own renders open, so flipping the raw `undefined` would write `true` and the
    // first click on its Collapse button would do nothing.
    const isOpen = effectiveExpanded[id] === true
    setExpanded((current) => ({ ...current, [id]: !isOpen }))
  }

  const select = (id: string) => {
    setSelectedId(id)
    setPanelOpen(true)
  }

  const clearReveal = useCallback(() => setRevealId(null), [])

  /** Open every branch above a member, scroll to it, select it, and open its panel. Shared by a
   *  clicked search hit and by the initial `?memberId=` reveal effect below — both need the same
   *  three things done together, just triggered differently. */
  const revealMember = useCallback(
    (id: string) => {
      setExpanded((current) => {
        const opened = { ...current }
        ancestorIds(roots, id).forEach((ancestor) => {
          opened[ancestor] = true
        })
        return opened
      })
      setRootOpen(true)
      setSelectedId(id)
      setPanelOpen(true)
      // Expanding ancestors used to be enough — the row was always in the DOM. Windowed, it may
      // not be, so the canvas is asked to scroll to it once this render settles.
      setRevealId(id)
    },
    [roots],
  )

  /** Reveal a search hit: same as revealMember, plus clearing the search query. */
  const revealResult = (id: string) => {
    revealMember(id)
    setQuery('')
  }

  // Completes the `?memberId=` preselection once the tree has actually loaded: the lazy
  // initialisers above already highlighted the row and opened the panel optimistically, but
  // expanding the member's ancestor chain needs the tree data, which isn't in yet at mount. Runs
  // at most once — `initialMemberId` is cleared right after, so this never re-fires and never
  // undoes a selection the user made in the meantime. An id matching no member degrades to the
  // plain tree: the optimistic selection/panel are rolled back instead of pointing at nothing.
  useEffect(() => {
    const id = initialMemberId.current
    if (id === null || view === undefined) return
    initialMemberId.current = null
    if (findNode(roots, id) === undefined) {
      setSelectedId(null)
      setPanelOpen(false)
      return
    }
    revealMember(id)
  }, [view, roots, revealMember])

  const openModal = (
    kind: ModalKind,
    initialName = '',
    initialLife = EMPTY_LIFE_DETAILS,
    initialContact = EMPTY_CONTACT_DETAILS,
  ) => {
    setModal(kind)
    setNameValue(initialName)
    setLifeValue(initialLife)
    setContactValue(initialContact)
    setErrorCode(null)
    setMenu(null)
  }

  /**
   * Opening the editor must show what is already on file. Update replaces rather than merges,
   * so a field left blank here is a field cleared on save — the panel's Edit button used to
   * seed only the name, which quietly wiped a member's dates the moment anyone renamed them
   * from the detail panel.
   */
  const openEdit = () => {
    const detail = selectedId === null ? undefined : detailById.get(selectedId)
    openModal(
      'edit',
      selected?.name ?? '',
      detail?.life ?? EMPTY_LIFE_DETAILS,
      detail?.contact ?? EMPTY_CONTACT_DETAILS,
    )
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
        { name: nameValue, parentId, life: lifeValue, contact: contactValue },
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
        {
          id: selected.id,
          name: nameValue,
          version: detail.version,
          life: lifeValue,
          contact: contactValue,
        },
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

  const confirmMove = (parentId: string | null) => {
    if (selected === undefined) return
    // The version comes from the flat list, the same join the editor reads — a move is a
    // write like any other and must carry the version the client actually held.
    const version = detailById.get(selected.id)?.version
    if (version === undefined) return

    setErrorCode(null)
    moveMember.mutate(
      { id: selected.id, parentId, version },
      {
        onSuccess: () => {
          setMoveOpen(false)
          showToast(t('toast.moved', { name: fullName(selected, byId) }))
        },
        // The dialog stays open on failure: the user's next act is to choose a different
        // target, and closing would make them find the member again first.
        onError: (error) => setErrorCode(codeOf(error)),
      },
    )
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
      <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
        {/* Above the canvas rather than inside it: the canvas scrolls, and a filter bar that
            scrolls away leaves the user unable to clear what is hiding half their family. */}
        <div
          style={{
            padding: 'var(--space-4) var(--space-6)',
            borderBottom: '1px solid var(--border)',
            background: 'var(--surface)',
          }}
        >
          <FilterControls
            filters={filters}
            activeCount={activeCount}
            onChange={setFilter}
            onReset={reset}
          />
        </div>

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
      </div>

      {panelOpen && selected !== undefined && (
        <MemberPanel
          member={selected}
          parentName={parentNameOf(selected)}
          lineage={lineageName(selected, byId)}
          rootGeneration={rootGeneration}
          createdAt={detail?.createdAt}
          updatedAt={detail?.updatedAt}
          life={detail?.life}
          permissions={permissions}
          onClose={() => {
            setPanelOpen(false)
            setSelectedId(null)
          }}
          onAdd={() => openModal('add')}
          onEdit={openEdit}
          onMove={() => setMoveOpen(true)}
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
          onEdit={openEdit}
          onMove={() => {
            setMenu(null)
            setMoveOpen(true)
          }}
          onDelete={() => {
            if (selected !== undefined) openDelete(selected)
          }}
        />
      )}

      {modal !== null && (
        <MemberModal
          kind={modal}
          subjectName={selected === undefined ? familyName : fullName(selected, byId)}
          parentName={modal === 'add' ? (selected?.name ?? familyName) : parentNameOf(selected)}
          childNames={selected?.children.map((child) => child.name) ?? []}
          nameValue={nameValue}
          // Editing shows the member's own ancestry; adding shows the ancestry the new member
          // is about to inherit, which is the selected node's own name followed by theirs.
          lineage={
            modal === 'add'
              ? selected === undefined
                ? ''
                : nameParts(selected, byId).slice(0, NAME_PART_COUNT - 1).join(' ')
              : selected === undefined
                ? ''
                : lineageName(selected, byId)
          }
          lifeValue={lifeValue}
          contactValue={contactValue}
          countries={countries ?? []}
          errorCode={errorCode}
          isSaving={createMember.isPending || updateMember.isPending || deleteMember.isPending}
          onNameChange={setNameValue}
          onLifeChange={setLifeValue}
          onContactChange={setContactValue}
          onCancel={closeModal}
          onConfirm={confirm}
        />
      )}

      {moveOpen && selected !== undefined && (
        <MoveDialog
          member={selected}
          familyName={familyName}
          // The member and everyone beneath them. Computed here because the page holds the
          // tree; the dialog only knows the member it was handed.
          blockedIds={new Set([selected.id, ...descendantIds(roots, selected.id)])}
          errorCode={errorCode}
          isSaving={moveMember.isPending}
          onCancel={() => {
            setMoveOpen(false)
            setErrorCode(null)
          }}
          onConfirm={confirmMove}
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
