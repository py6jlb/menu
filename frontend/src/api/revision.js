/**
 * Ревизия ресурса из тела ответа. Сервер отдаёт целое число; всё остальное
 * (поле отсутствует, тело не разобралось, значение не число) означает
 * «ревизия неизвестна» — `null`, а не `0` или `NaN`.
 */
export function revisionOf(data) {
  return Number.isInteger(data?.revision) ? data.revision : null
}
