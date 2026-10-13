import { apiJson } from './client'

/** Список покупок за неделю с понедельником `weekStart` (ISO). */
export async function getShoppingList(weekStart) {
  return apiJson(`/api/shopping-list?weekStart=${weekStart}`)
}