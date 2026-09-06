import { apiJson } from './client'

export async function getMyFamily() {
  return apiJson('/api/families/my')
}

export async function createFamily(name) {
  return apiJson('/api/families', {
    method: 'POST',
    body: JSON.stringify({ name })
  })
}

export async function joinFamily(inviteCode) {
  return apiJson('/api/families/join', {
    method: 'POST',
    body: JSON.stringify({ inviteCode })
  })
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
