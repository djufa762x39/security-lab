import { apiRequest, type ApiResult } from './http'
import type { LoginHistoryFilter, LoginHistoryPage } from '../types/loginHistory'
// IDN-22 TIM 09

// UX limits only; the server validates every request again and is the only real check (SEC-05).
export const LOGIN_HISTORY_PAGE_SIZES = [10, 20, 50] as const
export const DEFAULT_PAGE_SIZE = 20
export const MAX_RANGE_DAYS = 90
export const MS_PER_DAY = 24 * 60 * 60 * 1000

const LOGIN_HISTORY_PATH = '/api/auth/login-history'
const DATE_INPUT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

/**
 * IDN-22: reads the caller's own login history. Only the whitelisted filter fields are copied into the query,
 * so a userId/subjectId can never reach the server from here (SEC-01).
 */
export function getLoginHistory(filter: LoginHistoryFilter): Promise<ApiResult<LoginHistoryPage>> {
  const params = new URLSearchParams()
  if (filter.from) params.set('from', filter.from)
  if (filter.to) params.set('to', filter.to)
  if (filter.outcome) params.set('outcome', filter.outcome)
  params.set('page', String(filter.page))
  params.set('pageSize', String(filter.pageSize))
  return apiRequest<LoginHistoryPage>('GET', `${LOGIN_HISTORY_PATH}?${params}`)
}

function localDate(value: string, hours: number, minutes: number, seconds: number, ms: number): string | undefined {
  const match = DATE_INPUT_PATTERN.exec(value)
  if (!match) return undefined
  const [year, month, day] = [Number(match[1]), Number(match[2]) - 1, Number(match[3])]
  const date = new Date(year, month, day, hours, minutes, seconds, ms)
  // Rejects rollover such as 2026-02-30 becoming March 2.
  if (date.getFullYear() !== year || date.getMonth() !== month || date.getDate() !== day) return undefined
  return date.toISOString()
}

/** Start of a local calendar day (`YYYY-MM-DD` from `<input type="date">`) as UTC ISO 8601; undefined when empty or invalid. */
export function localDayStartIso(value: string): string | undefined {
  return localDate(value, 0, 0, 0, 0)
}

/** End of a local calendar day (23:59:59.999) as UTC ISO 8601; the server treats both bounds as inclusive. */
export function localDayEndIso(value: string): string | undefined {
  return localDate(value, 23, 59, 59, 999)
}
