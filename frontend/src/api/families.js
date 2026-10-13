import { apiJson, postJson } from './client'

export async function getMyFamily() {
  return apiJson('/api/families/my')
}

export async function createFamily(name) {
  return postJson('/api/families', { name })
}

export async function joinFamily(inviteCode) {
  return postJson('/api/families/join', { inviteCode })
}

export async function regenerateInviteCode(familyId) {
  return apiJson(`/api/families/${familyId}/invite-code/regenerate`, {
    method: 'POST'
  })
}

export async function removeMember(familyId, userId) {
  return apiJson(`/api/families/${familyId}/members/${userId}`, {
    method: 'DELETE'
  })
}
