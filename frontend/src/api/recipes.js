import { apiJson } from './client'

export async function listRecipes(scope = 'all') {
  const query = scope && scope !== 'all' ? `?scope=${encodeURIComponent(scope)}` : ''
  return apiJson(`/api/recipes${query}`)
}

export async function matchRecipes(body) {
  return apiJson('/api/recipes/match', {
    method: 'POST',
    body: JSON.stringify(body)
  })
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

function revisionQuery(revision) {
  return revision === null || revision === undefined ? '' : `?revision=${encodeURIComponent(revision)}`
}

export async function deleteRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}${revisionQuery(revision)}`, {
    method: 'DELETE'
  })
}

export async function removeExternalRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/external${revisionQuery(revision)}`, {
    method: 'DELETE'
  })
}

export async function copyRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/copy${revisionQuery(revision)}`, {
    method: 'POST'
  })
}

export async function uploadRecipePhoto(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/photo${revisionQuery(revision)}`, {
    method: 'PUT',
    body: form
  })
}

export async function deleteRecipePhoto(id, revision) {
  return apiJson(`/api/recipes/${id}/photo${revisionQuery(revision)}`, {
    method: 'DELETE'
  })
}

export async function uploadRecipeDocument(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/document${revisionQuery(revision)}`, {
    method: 'PUT',
    body: form
  })
}

export async function deleteRecipeDocument(id, revision) {
  return apiJson(`/api/recipes/${id}/document${revisionQuery(revision)}`, {
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