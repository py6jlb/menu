import { apiJson, putJson, revisionQuery } from './client'

export async function getWeekPlan(weekStart) {
  return apiJson(`/api/plans/week/${weekStart}`)
}

export async function saveWeekPlan(weekStart, entries, expectedRevision = 0) {
  return putJson(`/api/plans/week/${weekStart}`, { entries, expectedRevision })
}

export async function deleteWeekPlan(weekStart, expectedRevision) {
  return apiJson(`/api/plans/week/${weekStart}${revisionQuery('expectedRevision', expectedRevision)}`, {
    method: 'DELETE'
  })
}
