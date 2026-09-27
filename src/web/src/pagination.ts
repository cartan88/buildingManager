export const PAGE_SIZES = [20, 50, 100] as const

export interface PageInfo { page: number; pageCount: number; start: number; end: number }

/**
 * Which slice of `total` rows to show. `page` is 1-based; null means the last page (where a
 * chronological list keeps its newest rows). Out-of-range pages are clamped.
 */
export function pageInfo(total: number, page: number | null, size: number): PageInfo {
  const pageCount = Math.max(1, Math.ceil(total / size))
  const current = Math.min(Math.max(page ?? pageCount, 1), pageCount)
  const start = (current - 1) * size
  return { page: current, pageCount, start, end: Math.min(start + size, total) }
}
