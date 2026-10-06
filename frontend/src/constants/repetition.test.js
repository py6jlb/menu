import { describe, it, expect } from 'vitest'
import { repetitionChip, repetitionTitle, repetitionWindowNote } from './repetition'

describe('словарь повторяемости', () => {
  it('подпись говорит о неделях, а не о числе приёмов пищи', () => {
    expect(repetitionChip(4)).toBe('🔁 4 нед.')
  })

  it('пояснение уточняет: разные недели, повтор внутри недели — один раз', () => {
    expect(repetitionTitle()).toContain('разных неделях')
    expect(repetitionTitle()).toContain('один раз')
  })

  it('пояснение подбора называет выбранную неделю и размер окна', () => {
    const note = repetitionWindowNote(3)
    expect(note).toContain('выбранной')
    expect(note).toContain('3')
    expect(note).toContain('один раз')
  })
})
