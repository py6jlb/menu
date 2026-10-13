import { apiJson, postJson, putJson, revisionQuery } from './client'

export async function listRecipes(scope = 'all') {
  const query = scope && scope !== 'all' ? `?scope=${encodeURIComponent(scope)}` : ''
  return apiJson(`/api/recipes${query}`)
}

export async function matchRecipes(body) {
  return postJson('/api/recipes/match', body)
}

export async function getRecipe(id) {
  return apiJson(`/api/recipes/${id}`)
}

export async function createRecipe(recipe) {
  return postJson('/api/recipes', recipe)
}

export async function updateRecipe(id, recipe) {
  return putJson(`/api/recipes/${id}`, recipe)
}

export async function deleteRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

export async function removeExternalRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/external${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

export async function copyRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/copy${revisionQuery('revision', revision)}`, {
    method: 'POST'
  })
}

export async function uploadRecipePhoto(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/photo${revisionQuery('revision', revision)}`, {
    method: 'PUT',
    body: form
  })
}

export async function deleteRecipePhoto(id, revision) {
  return apiJson(`/api/recipes/${id}/photo${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

export async function uploadRecipeDocument(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/document${revisionQuery('revision', revision)}`, {
    method: 'PUT',
    body: form
  })
}

export async function deleteRecipeDocument(id, revision) {
  return apiJson(`/api/recipes/${id}/document${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

export async function getSharedRecipe(token) {
  return apiJson(`/api/shared/${token}`)
}

export async function importSharedRecipe(token) {
  return apiJson(`/api/shared/${token}/import`, {
    method: 'POST'
  })
}

export async function getRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`)
}

export async function createRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`, {
    method: 'POST'
  })
}

export async function revokeRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`, {
    method: 'DELETE'
  })
}

export async function regenerateRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share/regenerate`, {
    method: 'POST'
  })
}
