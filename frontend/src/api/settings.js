import { apiJson, putJson } from './client'

/** Личные настройки пользователя. */
export async function getSettings() {
  return apiJson('/api/settings')
}

/** Сохранение личных настроек. */
export async function updateSettings(settings) {
  return putJson('/api/settings', settings)
}