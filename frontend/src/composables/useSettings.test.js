import { describe, it, expect, vi } from 'vitest'
import { useSettings, SETTINGS_LOAD_ERROR, SETTINGS_SAVE_ERROR } from './useSettings'
import { DEFAULT_WINDOW_WEEKS } from '../constants/settings'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(weeks) {
  return { response: { status: 200 }, data: { repetitionWindowWeeks: weeks } }
}

function make(options = {}) {
  return useSettings(options)
}

describe('useSettings — загрузка', () => {
  it('применяет серверное значение и снимает pending', async () => {
    const state = make({ loadSettings: vi.fn().mockResolvedValue(ok(7)) })

    await state.load()

    expect(state.windowWeeks.value).toBe(7)
    expect(state.confirmedWindowWeeks.value).toBe(7)
    expect(state.dirty.value).toBe(false)
    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe('')
  })

  it('сетевой отказ завершает pending, сохраняет ввод и позволяет повторить', async () => {
    const loadSettings = vi
      .fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(ok(5))
    const state = make({ loadSettings })

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(SETTINGS_LOAD_ERROR)
    expect(state.windowWeeks.value).toBe(DEFAULT_WINDOW_WEEKS)

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe('')
    expect(state.windowWeeks.value).toBe(5)
  })

  it('неразобранный успешный ответ даёт понятную ошибку, не трогая значение', async () => {
    const state = make({ loadSettings: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null }) })

    await state.load()

    expect(state.loadError.value).toBe(SETTINGS_LOAD_ERROR)
    expect(state.windowWeeks.value).toBe(DEFAULT_WINDOW_WEEKS)
    expect(state.loading.value).toBe(false)
  })

  it('сообщение сервера используется при отказе', async () => {
    const state = make({
      loadSettings: vi.fn().mockResolvedValue({
        response: { status: 400 },
        data: { error: 'Серверная ошибка' }
      })
    })

    await state.load()

    expect(state.loadError.value).toBe('Серверная ошибка')
  })
})

describe('useSettings — сохранение', () => {
  it('подтверждает сохранение фактическим серверным значением', async () => {
    const saveSettings = vi.fn().mockResolvedValue(ok(6))
    const state = make({ saveSettings })
    state.windowWeeks.value = 10

    await state.save()

    expect(saveSettings).toHaveBeenCalledWith({ repetitionWindowWeeks: 10 })
    expect(state.confirmedWindowWeeks.value).toBe(6)
    expect(state.windowWeeks.value).toBe(6)
    expect(state.savedMessage.value).toMatch(/сохранены/i)
    expect(state.saving.value).toBe(false)
    expect(state.dirty.value).toBe(false)
  })

  it('сетевой отказ сохраняет введённое значение и даёт повтор', async () => {
    const saveSettings = vi
      .fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(ok(8))
    const state = make({ saveSettings })
    state.windowWeeks.value = 8

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SETTINGS_SAVE_ERROR)
    expect(state.windowWeeks.value).toBe(8)

    await state.save()

    expect(state.saveError.value).toBe('')
    expect(state.savedMessage.value).toMatch(/сохранены/i)
  })

  it('неразобранный ответ не подтверждает сохранение и сохраняет ввод', async () => {
    const state = make({
      saveSettings: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null })
    })
    state.windowWeeks.value = 9

    await state.save()

    expect(state.saveError.value).toBe(SETTINGS_SAVE_ERROR)
    expect(state.windowWeeks.value).toBe(9)
    expect(state.confirmedWindowWeeks.value).toBeNull()
    expect(state.saving.value).toBe(false)
  })

  it('правка во время сохранения не теряется', async () => {
    const pending = deferred()
    const state = make({ saveSettings: vi.fn().mockImplementation(() => pending.promise) })
    state.windowWeeks.value = 4

    const savePromise = state.save()
    state.windowWeeks.value = 12

    pending.resolve(ok(4))
    await savePromise

    expect(state.windowWeeks.value).toBe(12)
    expect(state.confirmedWindowWeeks.value).toBe(4)
    expect(state.dirty.value).toBe(true)
  })

  it('устаревший ответ сохранения не подменяет более новую правку', async () => {
    const first = deferred()
    const second = deferred()
    const saveSettings = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)
    const state = make({ saveSettings })
    state.windowWeeks.value = 4

    const firstSave = state.save()
    state.windowWeeks.value = 20
    const secondSave = state.save()

    second.resolve(ok(20))
    await secondSave
    first.resolve(ok(4))
    await firstSave

    expect(state.windowWeeks.value).toBe(20)
    expect(state.confirmedWindowWeeks.value).toBe(20)
    expect(state.dirty.value).toBe(false)
    expect(state.saving.value).toBe(false)
  })

  it('ошибка сервера показывается без подмены ввода', async () => {
    const state = make({
      saveSettings: vi.fn().mockResolvedValue({
        response: { status: 400 },
        data: { error: 'Окно вне границ' }
      })
    })
    state.windowWeeks.value = 999

    await state.save()

    expect(state.saveError.value).toBe('Окно вне границ')
    expect(state.windowWeeks.value).toBe(999)
  })
})
