import type { ReportOwner } from './api'

/** Which breakdown a report shows. */
export type ReportSide = 'expense' | 'income'

/** ?owner= of a report page: "family", else "me" (the default). */
export const reportOwnerFromParams = (params: URLSearchParams): ReportOwner =>
  params.get('owner') === 'family' ? 'family' : 'me'

/** ?side= of a report page: "income", else "expense" (the default). */
export const reportSideFromParams = (params: URLSearchParams): ReportSide =>
  params.get('side') === 'income' ? 'income' : 'expense'
