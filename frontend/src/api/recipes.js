import { apiJson, postJson, putJson, revisionQuery } from './client'

/** Список рецептов; `scope` сужает выборку до своих/семейных/внешних. */
export async function listRecipes(scope = 'all') {
  const query = scope && scope !== 'all' ? `?scope=${encodeURIComponent(scope)}` : ''
  return apiJson(`/api/recipes${query}`)
}

/** Подбор блюд по фильтрам, поиску и окну повторяемости. */
export async function matchRecipes(body) {
  return postJson('/api/recipes/match', body)
}

/** Рецепт по id. */
export async function getRecipe(id) {
  return apiJson(`/api/recipes/${id}`)
}

/** Создание рецепта. */
export async function createRecipe(recipe) {
  return postJson('/api/recipes', recipe)
}

/** Обновление рецепта по id. */
export async function updateRecipe(id, recipe) {
  return putJson(`/api/recipes/${id}`, recipe)
}

/** Удаление рецепта; ревизия защищает от удаления чужой правки. */
export async function deleteRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

/** Убрать внешний (импортированный) рецепт, не трогая его у источника. */
export async function removeExternalRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/external${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

/** Скопировать доступный рецепт в свои (создаёт независимую копию). */
export async function copyRecipe(id, revision) {
  return apiJson(`/api/recipes/${id}/copy${revisionQuery('revision', revision)}`, {
    method: 'POST'
  })
}

/** Загрузить/заменить фото рецепта; `revision` — ожидаемая версия. */
export async function uploadRecipePhoto(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/photo${revisionQuery('revision', revision)}`, {
    method: 'PUT',
    body: form
  })
}

/** Удалить фото рецепта. */
export async function deleteRecipePhoto(id, revision) {
  return apiJson(`/api/recipes/${id}/photo${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

/** Загрузить/заменить PDF-документ рецепта. */
export async function uploadRecipeDocument(id, file, revision) {
  const form = new FormData()
  form.append('file', file)
  return apiJson(`/api/recipes/${id}/document${revisionQuery('revision', revision)}`, {
    method: 'PUT',
    body: form
  })
}

/** Удалить PDF-документ рецепта. */
export async function deleteRecipeDocument(id, revision) {
  return apiJson(`/api/recipes/${id}/document${revisionQuery('revision', revision)}`, {
    method: 'DELETE'
  })
}

/** Публичный рецепт по токену ссылки (без авторизации). */
export async function getSharedRecipe(token) {
  return apiJson(`/api/shared/${token}`)
}

/** Импортировать публичный рецепт в свои по токену ссылки. */
export async function importSharedRecipe(token) {
  return apiJson(`/api/shared/${token}/import`, {
    method: 'POST'
  })
}

/** Текущая ссылка публичного доступа к рецепту, если она есть. */
export async function getRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`)
}

/** Создать ссылку публичного доступа к рецепту. */
export async function createRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`, {
    method: 'POST'
  })
}

/** Отозвать ссылку публичного доступа; прежний токен перестаёт работать. */
export async function revokeRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share`, {
    method: 'DELETE'
  })
}

/** Перегенерировать ссылку публичного доступа, не отзывая её. */
export async function regenerateRecipeShare(id) {
  return apiJson(`/api/recipes/${id}/share/regenerate`, {
    method: 'POST'
  })
}
