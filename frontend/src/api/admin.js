import { postJson } from './client'

export async function unlockUser(email) {
  return postJson('/api/admin/unlock', { email })
}
