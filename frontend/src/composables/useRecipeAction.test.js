import { describe, it, expect, vi } from 'vitest'
import { useRecipeAction } from './useRecipeAction'
import { CONFLICT, SERVER_UNAVAILABLE } from '../constants/messages'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function reply(status, data = null) {
  return { response: { status }, data }
}

describe('useRecipeAction — исход по статусу', () => {
  it('принятый статус зовёт onSuccess с данными и не оставляет ошибки', async () => {
    const action = useRecipeAction()
    const onSuccess = vi.fn()

    await action.run(() => Promise.resolve(reply(204)), {
      key: 'r1',
      acceptedStatuses: [204],
      onSuccess,
      fallbackMessage: 'фолбэк'
    })

    expect(onSuccess).toHaveBeenCalledWith(null)
    expect(action.error.value).toBe('')
    expect(action.conflict.value).toBeNull()
    expect(action.pending.value).toBe(false)
  })

  it('несколько принятых статусов: 201 отдаёт тело в onSuccess', async () => {
    const action = useRecipeAction()
    const onSuccess = vi.fn()
    const copy = { id: 'copy', revision: 3 }

    await action.run(() => Promise.resolve(reply(201, copy)), {
      key: 'r1',
      acceptedStatuses: [200, 201],
      onSuccess
    })

    expect(onSuccess).toHaveBeenCalledWith(copy)
    expect(action.error.value).toBe('')
  })

  it('409 заполняет conflict ревизией и показывает серверный текст', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(409, { error: 'Конфликт', revision: 7 })), {
      key: 'r1',
      acceptedStatuses: [204]
    })

    expect(action.conflict.value).toEqual({ revision: 7 })
    expect(action.error.value).toBe('Конфликт')
    expect(action.pending.value).toBe(false)
  })

  it('409 без серверного текста даёт канонический CONFLICT и null-ревизию', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(409, {})), {
      key: 'r1',
      acceptedStatuses: [204]
    })

    expect(action.conflict.value).toEqual({ revision: null })
    expect(action.error.value).toBe(CONFLICT)
  })

  it('409 без целой ревизии не подменяет её нулём', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(409, { error: 'Конфликт', revision: 'нет' })), {
      key: 'r1',
      acceptedStatuses: [204]
    })

    expect(action.conflict.value).toEqual({ revision: null })
  })

  it('неожиданный статус показывает серверный текст', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(403, { error: 'Нет прав' })), {
      key: 'r1',
      acceptedStatuses: [204],
      fallbackMessage: 'фолбэк'
    })

    expect(action.error.value).toBe('Нет прав')
    expect(action.conflict.value).toBeNull()
  })

  it('неожиданный статус без текста показывает fallbackMessage', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(500)), {
      key: 'r1',
      acceptedStatuses: [204],
      fallbackMessage: 'Не удалось удалить рецепт.'
    })

    expect(action.error.value).toBe('Не удалось удалить рецепт.')
    expect(action.conflict.value).toBeNull()
  })

  it('сетевая ошибка даёт SERVER_UNAVAILABLE и снимает pending', async () => {
    const action = useRecipeAction()

    await action.run(() => Promise.reject(new Error('offline')), {
      key: 'r1',
      acceptedStatuses: [204]
    })

    expect(action.error.value).toBe(SERVER_UNAVAILABLE)
    expect(action.pending.value).toBe(false)
  })
})

describe('useRecipeAction — pending', () => {
  it('pending поднимается на время запроса и снимается после ответа', async () => {
    const d = deferred()
    const action = useRecipeAction()

    const running = action.run(() => d.promise, { key: 'r1', acceptedStatuses: [204] })
    expect(action.pending.value).toBe(true)

    d.resolve(reply(204))
    await running

    expect(action.pending.value).toBe(false)
  })

  it('новый запуск очищает прежнюю ошибку и конфликт', async () => {
    const d = deferred()
    const action = useRecipeAction()

    await action.run(() => Promise.resolve(reply(409, { error: 'Конфликт', revision: 1 })), {
      key: 'r1',
      acceptedStatuses: [204]
    })
    expect(action.error.value).toBe('Конфликт')

    const running = action.run(() => d.promise, { key: 'r1', acceptedStatuses: [204] })
    expect(action.error.value).toBe('')
    expect(action.conflict.value).toBeNull()

    d.resolve(reply(204))
    await running
  })
})

describe('useRecipeAction — страж identity ресурса', () => {
  it('ответ старого ключа не применяется после запуска по новому ключу', async () => {
    const slow = deferred()
    const action = useRecipeAction()
    const onSuccess = vi.fn()

    const first = action.run(() => slow.promise, {
      key: 'old',
      acceptedStatuses: [200],
      onSuccess
    })
    await action.run(() => Promise.resolve(reply(200, { id: 'new' })), {
      key: 'new',
      acceptedStatuses: [200]
    })

    slow.resolve(reply(200, { id: 'old' }))
    await first

    expect(onSuccess).not.toHaveBeenCalled()
    expect(action.error.value).toBe('')
    expect(action.pending.value).toBe(false)
  })

  it('409 старого ключа не поднимает конфликт', async () => {
    const slow = deferred()
    const action = useRecipeAction()

    const first = action.run(() => slow.promise, { key: 'old', acceptedStatuses: [204] })
    await action.run(() => Promise.resolve(reply(204)), { key: 'new', acceptedStatuses: [204] })

    slow.resolve(reply(409, { error: 'Конфликт', revision: 1 }))
    await first

    expect(action.conflict.value).toBeNull()
    expect(action.error.value).toBe('')
  })

  it('reset обесценивает незавершённый запрос и чистит состояние', async () => {
    const d = deferred()
    const action = useRecipeAction()
    const onSuccess = vi.fn()

    const running = action.run(() => d.promise, {
      key: 'r1',
      acceptedStatuses: [200],
      onSuccess
    })
    action.reset()

    d.resolve(reply(200, { id: 'r1' }))
    await running

    expect(onSuccess).not.toHaveBeenCalled()
    expect(action.error.value).toBe('')
    expect(action.conflict.value).toBeNull()
    expect(action.pending.value).toBe(false)
  })
})
