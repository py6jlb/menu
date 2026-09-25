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

export function externalState(state) {
  if (!state) return null
  return EXTERNAL_STATES[state] || null
}
