import { describe, it, expect } from 'vitest'
import { revisionOf } from './revision'

describe('revisionOf', () => {
  it('возвращает целую ревизию', () => {
    expect(revisionOf({ revision: 0 })).toBe(0)
    expect(revisionOf({ revision: 7 })).toBe(7)
  })

  it('отсутствующее или неразобранное значение — null', () => {
    expect(revisionOf(null)).toBeNull()
    expect(revisionOf(undefined)).toBeNull()
    expect(revisionOf({})).toBeNull()
    expect(revisionOf({ revision: null })).toBeNull()
    expect(revisionOf({ revision: 1.5 })).toBeNull()
    expect(revisionOf({ revision: '3' })).toBeNull()
  })
})
