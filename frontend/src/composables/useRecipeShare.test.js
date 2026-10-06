import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('../api/recipes', () => ({
  getRecipeShare: vi.fn(),
  createRecipeShare: vi.fn(),
  revokeRecipeShare: vi.fn(),
  regenerateRecipeShare: vi.fn()
}))

import {
  getRecipeShare,
  createRecipeShare,
  revokeRecipeShare,
  regenerateRecipeShare
} from '../api/recipes'
import { useRecipeShare } from './useRecipeShare'

const RECIPE = 'recipe-1'
const shareDto = (token, extra = {}) => ({
  recipeId: RECIPE,
  token,
  url: `https://menu.example.com/r/${token}`,
  path: `/r/${token}`,
  createdAt: '2026-01-01T00:00:00Z',
  revoked: false,
  revokedAt: null,
  ...extra
})

beforeEach(() => {
  vi.clearAllMocks()
})

describe('useRecipeShare — чтение не создаёт ссылку', () => {
  it('автозагрузка читает существующую ссылку и не вызывает создание', async () => {
    getRecipeShare.mockResolvedValue({ response: { status: 200 }, data: shareDto('tok') })
    const state = useRecipeShare()

    await state.load(RECIPE)

    expect(getRecipeShare).toHaveBeenCalledWith(RECIPE)
    expect(createRecipeShare).not.toHaveBeenCalled()
    expect(state.share.value.token).toBe('tok')
    expect(state.error.value).toBe('')
  })

  it('отсутствие ссылки даёт пустое состояние без ошибки и без создания', async () => {
    getRecipeShare.mockResolvedValue({ response: { status: 404 }, data: { error: 'Ссылка ещё не создана.' } })
    const state = useRecipeShare()

    await state.load(RECIPE)

    expect(state.share.value).toBeNull()
    expect(state.error.value).toBe('')
    expect(createRecipeShare).not.toHaveBeenCalled()
  })
})

describe('useRecipeShare — явное создание', () => {
  it('POST со статусом 201 сохраняет ссылку и снимает загрузку', async () => {
    createRecipeShare.mockResolvedValue({ response: { status: 201 }, data: shareDto('new') })
    const state = useRecipeShare()

    await state.create(RECIPE)

    expect(createRecipeShare).toHaveBeenCalledWith(RECIPE)
    expect(state.share.value.token).toBe('new')
    expect(state.loading.value).toBe(false)
    expect(state.error.value).toBe('')
  })

  it('повторный POST со статусом 200 возвращает ту же ссылку', async () => {
    createRecipeShare.mockResolvedValue({ response: { status: 200 }, data: shareDto('same') })
    const state = useRecipeShare()

    await state.create(RECIPE)

    expect(state.share.value.token).toBe('same')
    expect(state.error.value).toBe('')
  })

  it('ограничение неподтверждённой почты показывается как ошибка', async () => {
    createRecipeShare.mockResolvedValue({
      response: { status: 403 },
      data: { error: 'Подтвердите почту, чтобы вносить изменения.' }
    })
    const state = useRecipeShare()

    await state.create(RECIPE)

    expect(state.share.value).toBeNull()
    expect(state.error.value).toBe('Подтвердите почту, чтобы вносить изменения.')
    expect(state.loading.value).toBe(false)
  })

  it('сетевой сбой не оставляет загрузку висеть', async () => {
    createRecipeShare.mockRejectedValue(new Error('offline'))
    const state = useRecipeShare()

    await state.create(RECIPE)

    expect(state.share.value).toBeNull()
    expect(state.error.value).toBe('Не удалось создать ссылку.')
    expect(state.loading.value).toBe(false)
  })
})

describe('useRecipeShare — отзыв и перегенерация', () => {
  it('отзыв помечает ссылку отозванной', async () => {
    revokeRecipeShare.mockResolvedValue({
      response: { status: 200 },
      data: shareDto('tok', { revoked: true, revokedAt: '2026-02-01T00:00:00Z' })
    })
    const state = useRecipeShare()

    await state.revoke(RECIPE)

    expect(revokeRecipeShare).toHaveBeenCalledWith(RECIPE)
    expect(state.share.value.revoked).toBe(true)
  })

  it('перегенерация заменяет токен', async () => {
    regenerateRecipeShare.mockResolvedValue({ response: { status: 200 }, data: shareDto('fresh') })
    const state = useRecipeShare()

    await state.regenerate(RECIPE)

    expect(regenerateRecipeShare).toHaveBeenCalledWith(RECIPE)
    expect(state.share.value.token).toBe('fresh')
  })
})

describe('useRecipeShare — identity ресурса', () => {
  it('поздний ответ старого рецепта не заполняет состояние нового', async () => {
    let resolveOld
    getRecipeShare.mockImplementationOnce(
      () => new Promise((resolve) => (resolveOld = resolve))
    )
    const state = useRecipeShare()

    const oldLoad = state.load('recipe-old')
    getRecipeShare.mockResolvedValueOnce({ response: { status: 404 }, data: {} })
    await state.load('recipe-new')

    resolveOld({ response: { status: 200 }, data: shareDto('old') })
    await oldLoad

    expect(state.share.value).toBeNull()
    expect(state.error.value).toBe('')
  })

  it('reset обесценивает незавершённую загрузку и очищает состояние', async () => {
    let resolveLoad
    getRecipeShare.mockImplementationOnce(
      () => new Promise((resolve) => (resolveLoad = resolve))
    )
    const state = useRecipeShare()

    const pending = state.load(RECIPE)
    state.reset()
    resolveLoad({ response: { status: 200 }, data: shareDto('late') })
    await pending

    expect(state.share.value).toBeNull()
    expect(state.error.value).toBe('')
  })
})
