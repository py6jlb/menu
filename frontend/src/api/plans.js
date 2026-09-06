import { apiJson } from './client'

export async function getWeekPlan(weekStart) {
  return apiJson(`/api/plans/week/${weekStart}`)
}

export async function saveWeekPlan(weekStart, entries) {
  return apiJson(`/api/plans/week/${weekStart}`, {
    method: 'PUT',
    body: JSON.stringify({ entries })
  })
}

export async function deleteWeekPlan(weekStart) {
  return apiJson(`/api/plans/week/${weekStart}`, {
    method: 'DELETE'
  })
}