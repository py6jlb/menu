/** Внешние состояния блюда и их подписи для бейджа и слота плана. */
export const EXTERNAL_STATES = {
  broken: {
    variant: 'broken',
    label: 'Недоступно',
    slotLabel: 'недоступно',
    slotTitle: 'Убрать или заменить',
    mobileLabel: 'недоступно — убрать или заменить'
  },
  warning: {
    variant: 'warning',
    label: 'Ссылка отозвана',
    slotLabel: 'ссылка отозвана',
    slotTitle: 'Ссылка отозвана',
    mobileLabel: 'ссылка отозвана'
  }
}

/** Описание внешнего состояния по коду; неизвестный код и пустое значение — `null`. */
export function externalState(state) {
  if (!state) return null
  return EXTERNAL_STATES[state] || null
}
