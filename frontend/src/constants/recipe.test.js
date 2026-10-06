import { describe, it, expect } from 'vitest'
import {
  DIETS,
  PHOTO_MAX_BYTES,
  PHOTO_TYPES,
  combineDiets,
  dietLabel,
  splitDiets,
  validatePhotoFile
} from './recipe'

describe('validatePhotoFile', () => {
  it('принимает поддерживаемые типы изображений в пределах размера', () => {
    for (const type of PHOTO_TYPES) {
      expect(validatePhotoFile({ type, size: 1024 })).toBe('')
    }
  })

  it('отклоняет неподдерживаемый тип до отправки', () => {
    expect(validatePhotoFile({ type: 'application/pdf', size: 1024 })).toMatch(/изображени/i)
  })

  it('отклоняет файл больше допустимого размера до отправки', () => {
    expect(validatePhotoFile({ type: 'image/jpeg', size: PHOTO_MAX_BYTES + 1 })).toMatch(/размер/i)
  })

  it('пустое значение не считается ошибкой', () => {
    expect(validatePhotoFile(null)).toBe('')
  })
})

describe('DIETS', () => {
  it('стандартные диеты имеют канонический код и русскую подпись', () => {
    expect(DIETS).toEqual([
      { code: 'vegetarian', label: 'Вегетарианское' },
      { code: 'gluten_free', label: 'Безглютеновое' },
      { code: 'lean', label: 'Постное' },
      { code: 'keto', label: 'Кетогенное' }
    ])
  })
})

describe('splitDiets', () => {
  it('разделяет стандартные коды и произвольные метки', () => {
    expect(splitDiets(['vegetarian', 'моя диета', 'lean'])).toEqual({
      selected: ['vegetarian', 'lean'],
      custom: ['моя диета']
    })
  })

  it('сохраняет порядок и отбрасывает повторы и пустые значения', () => {
    expect(splitDiets(['lean', 'vegetarian', 'lean', '', '  ', null])).toEqual({
      selected: ['lean', 'vegetarian'],
      custom: []
    })
  })

  it('неизвестные значения не теряются', () => {
    expect(splitDiets(['средиземноморское']).custom).toEqual(['средиземноморское'])
  })

  it('пустой вход даёт пустые списки', () => {
    expect(splitDiets(null)).toEqual({ selected: [], custom: [] })
  })
})

describe('combineDiets', () => {
  it('объединяет выбранные коды и произвольные метки', () => {
    expect(combineDiets(['vegetarian'], 'моя диета, lean')).toEqual([
      'vegetarian',
      'моя диета',
      'lean'
    ])
  })

  it('не добавляет повторные значения', () => {
    expect(combineDiets(['vegetarian'], 'vegetarian, моя диета')).toEqual([
      'vegetarian',
      'моя диета'
    ])
  })

  it('пустые входы не мешают', () => {
    expect(combineDiets(['lean'], '')).toEqual(['lean'])
    expect(combineDiets(null, 'моя диета')).toEqual(['моя диета'])
    expect(combineDiets(null, '')).toEqual([])
  })
})

describe('dietLabel', () => {
  it('переводит код в подпись, незнакомое значение отдаёт как есть', () => {
    expect(dietLabel('vegetarian')).toBe('Вегетарианское')
    expect(dietLabel('моя диета')).toBe('моя диета')
  })
})
