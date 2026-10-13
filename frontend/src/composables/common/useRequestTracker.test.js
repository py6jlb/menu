import { describe, it, expect } from 'vitest'
import { useRequestTracker } from './useRequestTracker'

describe('useRequestTracker — порядок ответов', () => {
  it('повторный begin по тому же ключу обесценивает прежний ответ (смена поколения)', () => {
    const tracker = useRequestTracker()

    const first = tracker.begin('week-1')
    const second = tracker.begin('week-1')

    expect(tracker.isCurrent(first)).toBe(false)
    expect(tracker.isCurrent(second)).toBe(true)
  })

  it('begin без ключа использует отдельный слот: каждый новый запрос снимает прежний', () => {
    const tracker = useRequestTracker()

    const first = tracker.begin()
    const second = tracker.begin()

    expect(tracker.isCurrent(first)).toBe(false)
    expect(tracker.isCurrent(second)).toBe(true)
  })

  it('null-токен никогда не актуален', () => {
    const tracker = useRequestTracker()

    expect(tracker.isCurrent(null)).toBe(false)
  })
})

describe('useRequestTracker — изоляция ключей', () => {
  it('begin по одному ключу не трогает токены другого ключа (строки)', () => {
    const tracker = useRequestTracker()

    const row1 = tracker.begin('row-1')
    const row2 = tracker.begin('row-2')

    expect(tracker.isCurrent(row1)).toBe(true)
    expect(tracker.isCurrent(row2)).toBe(true)
  })

  it('обновление строки обесценивает только её собственный токен', () => {
    const tracker = useRequestTracker()

    const row1 = tracker.begin('row-1')
    const row2 = tracker.begin('row-2')
    const row1Next = tracker.begin('row-1')

    expect(tracker.isCurrent(row1)).toBe(false)
    expect(tracker.isCurrent(row1Next)).toBe(true)
    expect(tracker.isCurrent(row2)).toBe(true)
  })
})

describe('useRequestTracker — reset', () => {
  it('reset() обесценивает все ключи', () => {
    const tracker = useRequestTracker()

    const withKey = tracker.begin('settings')
    const withoutKey = tracker.begin()

    tracker.reset()

    expect(tracker.isCurrent(withKey)).toBe(false)
    expect(tracker.isCurrent(withoutKey)).toBe(false)
  })

  it('reset(key) обесценивает только указанный ключ', () => {
    const tracker = useRequestTracker()

    const row1 = tracker.begin('row-1')
    const row2 = tracker.begin('row-2')

    tracker.reset('row-1')

    expect(tracker.isCurrent(row1)).toBe(false)
    expect(tracker.isCurrent(row2)).toBe(true)
  })

  it('после reset можно снова начать запрос с тем же ключом', () => {
    const tracker = useRequestTracker()

    const before = tracker.begin('row-1')
    tracker.reset('row-1')
    const after = tracker.begin('row-1')

    expect(tracker.isCurrent(before)).toBe(false)
    expect(tracker.isCurrent(after)).toBe(true)
  })
})
