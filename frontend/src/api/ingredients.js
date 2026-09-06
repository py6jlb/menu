import { apiJson } from './client'

export async function autocompleteIngredients(q) {
  const query = q ? `?q=${encodeURIComponent(q)}` : ''
  return apiJson(`/api/ingredients/autocomplete${query}`)
}
