import { ApiError } from './api'

/** Validation messages per request field (camelCase keys, as the API returns them); `form` = not tied to a field. */
export type Errors = Record<string, string[]>

export const GENERIC_ERROR = 'Something went wrong, try again.'

/**
 * Turns a failed API call into form errors: a 400's field errors as they are; anything else becomes one `form`
 * message (`forbidden` for a 403 when given, else the generic one).
 */
export function toErrors(err: unknown, forbidden?: string): Errors {
  if (err instanceof ApiError && err.status === 400) return err.fieldErrors
  if (forbidden && err instanceof ApiError && err.status === 403) return { form: [forbidden] }
  return { form: [GENERIC_ERROR] }
}

/** First message of any field (for small inline forms that show one line). */
export const firstError = (errors: Errors): string | undefined => Object.values(errors)[0]?.[0]
