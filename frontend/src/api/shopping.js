import { apiJson } from './client'

export async function getShoppingList(weekStart) {
  return apiJson(`/api/shopping-list?weekStart=${weekStart}`)
}