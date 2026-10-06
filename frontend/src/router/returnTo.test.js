import { describe, it, expect } from 'vitest'
import { sanitizeReturnTo, returnToQuery } from './returnTo'

describe('sanitizeReturnTo', () => {
  it('разрешает внутренний маршрут с query и hash', () => {
    expect(sanitizeReturnTo('/recipes/1?tab=plan#top')).toBe('/recipes/1?tab=plan#top')
  })

  it('отбрасывает абсолютные и протокол-относительные URL', () => {
    expect(sanitizeReturnTo('https://evil.example.com')).toBeNull()
    expect(sanitizeReturnTo('http://evil.example.com/x')).toBeNull()
    expect(sanitizeReturnTo('//evil.example.com')).toBeNull()
  })

  it('отбрасывает обратные слэши и относительные пути', () => {
    expect(sanitizeReturnTo('/\\evil')).toBeNull()
    expect(sanitizeReturnTo('recipes/1')).toBeNull()
  })

  it('отбрасывает пустые и нестроковые значения', () => {
    expect(sanitizeReturnTo('')).toBeNull()
    expect(sanitizeReturnTo(undefined)).toBeNull()
    expect(sanitizeReturnTo(['/a'])).toBeNull()
  })
})

describe('returnToQuery', () => {
  it('даёт query для внутреннего маршрута', () => {
    expect(returnToQuery('/plan?d=1')).toEqual({ returnTo: '/plan?d=1' })
  })

  it('не даёт query для внешнего маршрута', () => {
    expect(returnToQuery('https://evil.example.com')).toEqual({})
  })
})
