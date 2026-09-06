import { apiJson } from './client'

export async function getSettings() {
  return apiJson('/api/settings')
}

export async function updateSettings(settings) {
  return apiJson('/api/settings', {
    method: 'PUT',
    body: JSON.stringify(settings)
  })
}