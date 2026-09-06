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

export async function uploadRecipePhoto(id, file) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/photo`, {
    method: 'PUT',
    body: form
  })
}

export async function deleteRecipePhoto(id) {
  return apiJson(`/api/recipes/${id}/photo`, {
    method: 'DELETE'
  })
}