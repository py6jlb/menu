import { postJson } from './client'

/** Административная разблокировка пользователя по email. */
export async function unlockUser(email) {
  return postJson('/api/admin/unlock', { email })
}
