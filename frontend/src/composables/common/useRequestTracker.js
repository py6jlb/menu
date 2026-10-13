/**
 * Единый трекер запросов: «поздний ответ не должен применяться».
 *
 * Внутри — глобально монотонная последовательность, запоминаемая на каждый
 * ключ (`Map`), поэтому одним interface покрываются:
 * - «нет ключа» (settings/picker и одиночные последовательности),
 * - «неделя» (shopping/week),
 * - «id» ресурса (share/draft),
 * - «строка» автодополнения (autocomplete).
 *
 * `begin(key?)` выдаёт токен текущего поколения ключа; `isCurrent(token)`
 * говорит, что ответ всё ещё принадлежит актуальному поколению; `reset(key?)`
 * обесценивает один ключ или все сразу.
 *
 * Ключи изолированы, поэтому запрос по одной строке автодополнения не отменяет
 * запрос по другой. Смена identity (id ресурса, недели) — это смена поколения
 * внутри одного ключа либо явный `reset()`.
 */
export function useRequestTracker() {
  const counters = new Map()
  let sequence = 0

  function begin(key) {
    sequence += 1
    counters.set(key, sequence)
    return { key, seq: sequence }
  }

  function isCurrent(token) {
    return token != null && counters.get(token.key) === token.seq
  }

  function reset(key) {
    if (arguments.length === 0) counters.clear()
    else counters.delete(key)
  }

  return { begin, isCurrent, reset }
}
