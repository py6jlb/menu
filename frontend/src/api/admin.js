import { apiJson } from './client'

export async function unlockUser(email) {
  return apiJson('/api/admin/unlock', {
    method: 'POST',
    body: JSON.stringify({ email })
  })
}
