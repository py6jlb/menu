import { apiJson, putJson, revisionQuery } from './client'

/** Недельный план по ISO-дате понедельника `weekStart`; 404 — плана нет. */
export async function getWeekPlan(weekStart) {
  return apiJson(`/api/plans/week/${weekStart}`)
}

/** Сохранение недельного плана; 409 при устаревшей `expectedRevision`. */
export async function saveWeekPlan(weekStart, entries, expectedRevision = 0) {
  return putJson(`/api/plans/week/${weekStart}`, { entries, expectedRevision })
}

/** Удаление недельного плана; ревизия защищает от удаления чужой правки. */
export async function deleteWeekPlan(weekStart, expectedRevision) {
  return apiJson(`/api/plans/week/${weekStart}${revisionQuery('expectedRevision', expectedRevision)}`, {
    method: 'DELETE'
  })
}
