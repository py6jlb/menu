import { describe, it, expect } from 'vitest'
import { PHOTO_MAX_BYTES, PHOTO_TYPES, validatePhotoFile } from './recipe'

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
