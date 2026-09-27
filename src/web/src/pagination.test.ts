import { describe, expect, it } from 'vitest'
import { pageInfo } from './pagination'

describe('pageInfo', () => {
  it('defaults to the last page', () => {
    expect(pageInfo(45, null, 20)).toEqual({ page: 3, pageCount: 3, start: 40, end: 45 })
  })

  it('returns the requested page', () => {
    expect(pageInfo(45, 2, 20)).toEqual({ page: 2, pageCount: 3, start: 20, end: 40 })
  })

  it('clamps pages that no longer exist, e.g. after a bigger page size', () => {
    expect(pageInfo(45, 3, 50)).toEqual({ page: 1, pageCount: 1, start: 0, end: 45 })
    expect(pageInfo(45, 0, 20).page).toBe(1)
  })

  it('treats an empty list as one empty page', () => {
    expect(pageInfo(0, null, 20)).toEqual({ page: 1, pageCount: 1, start: 0, end: 0 })
  })
})
