/**
 * Mirrors FamilyTree.Contracts.Reports. Dates arrive as ISO strings: `DateOnly` serialises to
 * `YYYY-MM-DD` and `DateTimeOffset` to a full ISO timestamp.
 */

/** A member as a report row identifies one. The lineage is composed client-side — see fullName.ts. */
export interface MemberRef {
  id: string
  name: string
  parentId: string | null
}

export interface GenerationCount {
  generation: number
  count: number
}

export interface BranchSummary {
  id: string
  name: string
  descendantCount: number
  depth: number
}

export interface StructureReport {
  totalMembers: number
  depth: number
  generations: GenerationCount[]
  branches: BranchSummary[]
  membersWithChildren: number
  leafMembers: number
  averageChildrenPerParent: number
}

export interface GenerationLifeStatus {
  generation: number
  living: number
  deceased: number
}

export interface AgeBracketCount {
  bracket: string
  count: number
}

export interface LongevityStats {
  count: number
  minYears: number
  maxYears: number
  medianYears: number
}

export interface LifeStatusReport {
  living: number
  deceased: number
  byGeneration: GenerationLifeStatus[]
  livingAges: AgeBracketCount[]
  livingWithoutBirthDate: number
  /** Null when no deceased member holds both dates — not measurable, as distinct from zero. */
  longevity: LongevityStats | null
}

/** `count` is every affected member; `members` is capped. Render the count, never members.length. */
export interface CompletenessIssue {
  code: string
  count: number
  members: MemberRef[]
}

export interface CompletenessReport {
  totalMembers: number
  completeRecords: number
  issues: CompletenessIssue[]
}

export interface UpcomingBirthday {
  member: MemberRef
  dateOfBirth: string
  occurrence: string
  daysAway: number
  turningAge: number
}

export interface UpcomingAnniversary {
  member: MemberRef
  dateOfDeath: string
  occurrence: string
  daysAway: number
  years: number
}

export interface UpcomingReport {
  windowDays: number
  birthdayCount: number
  anniversaryCount: number
  birthdays: UpcomingBirthday[]
  anniversaries: UpcomingAnniversary[]
}

export interface ActivityEntry {
  member: MemberRef
  at: string
}

export interface ActivityReport {
  windowDays: number
  addedCount: number
  editedCount: number
  added: ActivityEntry[]
  edited: ActivityEntry[]
}

export interface ReportsResponse {
  /** The server's UTC reference day, `YYYY-MM-DD`. Never re-derive "today" locally. */
  generatedOn: string
  structure: StructureReport
  lifeStatus: LifeStatusReport
  completeness: CompletenessReport
  upcoming: UpcomingReport
  activity: ActivityReport
}
