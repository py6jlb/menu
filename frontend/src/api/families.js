import { apiJson, postJson } from './client'

/** Семья текущего пользователя; 404 означает, что он ещё не состоит в семье. */
export async function getMyFamily() {
  return apiJson('/api/families/my')
}

/** Создание семьи с названием `name`. */
export async function createFamily(name) {
  return postJson('/api/families', { name })
}

/** Вступление в семью по коду приглашения. */
export async function joinFamily(inviteCode) {
  return postJson('/api/families/join', { inviteCode })
}

/** Перегенерация кода приглашения семьи; прежний код перестаёт действовать. */
export async function regenerateInviteCode(familyId) {
  return apiJson(`/api/families/${familyId}/invite-code/regenerate`, {
    method: 'POST'
  })
}

/** Исключение участника из семьи. */
export async function removeMember(familyId, userId) {
  return apiJson(`/api/families/${familyId}/members/${userId}`, {
    method: 'DELETE'
  })
}
