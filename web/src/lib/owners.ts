import type { Owner, OwnerRef } from './api'

/** An Owner as a form / URL value: "family" or the Member's id. */
export const ownerValue = (owner: Owner | OwnerRef): string =>
  owner.kind === 'family' ? 'family' : (owner.memberId ?? 'family')

/** The request shape for a form / URL value made by {@link ownerValue}. */
export const ownerRefFromValue = (value: string): OwnerRef =>
  value === 'family' ? { kind: 'family' } : { kind: 'member', memberId: value }
