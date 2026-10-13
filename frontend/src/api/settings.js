import { apiJson, putJson } from './client'

export async function getSettings() {
  return apiJson('/api/settings')
}

export async function updateSettings(settings) {
  return putJson('/api/settings', settings)
}