import { apiJson } from './client'

export async function getWeekPlan(weekStart) {
  return apiJson(`/api/plans/week/${weekStart}`)
}

export async function saveWeekPlan(weekStart, entries, expectedRevision = 0) {
  return apiJson(`/api/plans/week/${weekStart}`, {
    method: 'PUT',
    body: JSON.stringify({ entries, expectedRevision })
  })
}

export async function deleteWeekPlan(weekStart, expectedRevision) {
  const query = expectedRevision === undefined ? '' : `?expectedRevision=${expectedRevision}`
  return apiJson(`/api/plans/week/${weekStart}${query}`, {
    method: 'DELETE'
  })
}
