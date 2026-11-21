/** A Member's names, as /api/auth/me and /api/admin/members return them. */
export type MemberNames = { userName: string; firstName: string; lastName: string | null }

/** First and last name: "Martin Škuta", or just "Martin". */
export const fullName = (m: MemberNames) => (m.lastName ? `${m.firstName} ${m.lastName}` : m.firstName)

/**
 * How Treasury names a Member in lists (the same label the API gives Owners): "Martin Š. (martin)" or
 * "Martin (martin)", so two Members with the same first name stay distinguishable.
 */
export const memberLabel = (m: MemberNames) =>
  m.lastName ? `${m.firstName} ${[...m.lastName][0]}. (${m.userName})` : `${m.firstName} (${m.userName})`

/** Avatar initials from first and last name: "Martin Škuta" -> "MŠ", "Martin" -> "M". */
export const initials = (m: MemberNames) =>
  [m.firstName, m.lastName ?? '']
    .map(n => [...n.trim()][0] ?? '')
    .join('')
    .toUpperCase()
