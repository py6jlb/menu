import { apiJson } from './client'

/** Подсказки ингредиентов по подстроке `q`; пустой `q` отдаёт полный список. */
export async function autocompleteIngredients(q) {
  const query = q ? `?q=${encodeURIComponent(q)}` : ''
  return apiJson(`/api/ingredients/autocomplete${query}`)
}
