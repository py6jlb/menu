import { apiJson } from './client'

export async function listRecipes() {
  return apiJson('/api/recipes')
}

export async function getRecipe(id) {
  return apiJson(`/api/recipes/${id}`)
}

export async function createRecipe(recipe) {
  return apiJson('/api/recipes', {
    method: 'POST',
    body: JSON.stringify(recipe)
  })
}

export async function updateRecipe(id, recipe) {
  return apiJson(`/api/recipes/${id}`, {
    method: 'PUT',
    body: JSON.stringify(recipe)
  })
}

export async function deleteRecipe(id) {
  return apiJson(`/api/recipes/${id}`, {
    method: 'DELETE'
  })
}