// Сквозной browser smoke «Меню для домохозяек».
//
// Запускается из scripts/browser-smoke.sh в контейнере с браузерами Playwright
// на сети dev-стека. Регистрация и подтверждение почты проходят через UI;
// семья, рецепт, план и покупки создаются изнутри браузера (тот же origin и
// сессия), затем рендер ключевых экранов и публичная ссылка проверяются в DOM.
//
// Ожидания ограничены и привязаны к событиям; коды подтверждения берутся из
// управляемого файла, который заполняет browser-smoke.sh из журнала backend.

import assert from 'node:assert/strict'
import fs from 'node:fs'
import { chromium } from 'playwright'

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://frontend'
const CODES_FILE = process.env.CODES_FILE || '/shared/codes.txt'
const EMAIL = process.env.SMOKE_EMAIL
const PASSWORD = process.env.SMOKE_PASSWORD
const STEP_TIMEOUT = 15000
const CODE_TIMEOUT = 60000

function readCodes() {
  if (!fs.existsSync(CODES_FILE)) return []
  return fs.readFileSync(CODES_FILE, 'utf8').trim().split(/\s+/).filter(Boolean)
}

async function waitForNewCode(previousCount) {
  const deadline = Date.now() + CODE_TIMEOUT
  while (Date.now() < deadline) {
    const codes = readCodes()
    if (codes.length > previousCount) return codes[codes.length - 1]
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  throw new Error('Код подтверждения не появился в журнале backend за отведённое время')
}

async function api(page, path, options = {}) {
  return page.evaluate(
    async ({ path, options }) => {
      const token = window.localStorage.getItem('menu_planner_token')
      const headers = { ...(options.headers || {}) }
      if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json'
      if (token) headers.Authorization = `Bearer ${token}`
      const response = await fetch(path, { ...options, headers })
      const text = await response.text()
      let data = null
      try {
        data = text ? JSON.parse(text) : null
      } catch {
        data = text
      }
      return { status: response.status, data }
    },
    { path, options }
  )
}

// Обращение к API напрямую из Node (для второго пользователя без UI-сессии).
async function apiDirect(path, { method = 'GET', token = '', body = null } = {}) {
  const headers = {}
  if (body) headers['Content-Type'] = 'application/json'
  if (token) headers.Authorization = `Bearer ${token}`
  const response = await fetch(`${FRONTEND_URL}${path}`, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined
  })
  const text = await response.text()
  let data = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = text
  }
  return { status: response.status, data }
}

function mondayIso() {
  const now = new Date()
  const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const offset = (monday.getDay() + 6) % 7
  monday.setDate(monday.getDate() - offset)
  const y = monday.getFullYear()
  const m = String(monday.getMonth() + 1).padStart(2, '0')
  const d = String(monday.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

async function main() {
  assert.ok(EMAIL && PASSWORD, 'SMOKE_EMAIL/SMOKE_PASSWORD не заданы')

  const browser = await chromium.launch()
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(STEP_TIMEOUT)

  try {
    // 1. Регистрация через UI.
    await page.goto(`${FRONTEND_URL}/register`, { waitUntil: 'domcontentloaded' })
    await page.getByLabel('Email').fill(EMAIL)
    await page.getByLabel('Пароль').fill(PASSWORD)
    await page.getByRole('button', { name: 'Зарегистрироваться' }).click()
    await page.waitForURL('**/verify', { timeout: STEP_TIMEOUT })

    // 2. Подтверждение почты через UI управляемым кодом из журнала.
    const code = await waitForNewCode(0)
    await page.getByLabel('Код из письма').fill(code)
    await page.getByRole('button', { name: 'Подтвердить' }).click()
    // Успех подтверждения: приложение уводит на главную.
    await page.waitForURL((url) => url.pathname === '/', { timeout: STEP_TIMEOUT })
    await page.goto(`${FRONTEND_URL}/verify`, { waitUntil: 'domcontentloaded' })
    await page.getByText('Почта уже подтверждена').waitFor({ state: 'visible' })
    await page.goto(`${FRONTEND_URL}/`, { waitUntil: 'domcontentloaded' })

    // 3. Семья и рецепт — изнутри браузера, тем же origin и сессией.
    const family = await api(page, '/api/families', {
      method: 'POST',
      body: JSON.stringify({ name: 'Смоук-семья' })
    })
    assert.ok([200, 201].includes(family.status), `Создание семьи: ${family.status}`)

    const recipeName = `Смоук-рецепт ${Date.now()}`
    const ingredient = 'Мука смоук'
    const created = await api(page, '/api/recipes', {
      method: 'POST',
      body: JSON.stringify({
        name: recipeName,
        description: 'Рецепт браузерного smoke',
        cookTimeMinutes: 30,
        servings: 4,
        difficulty: 1,
        calories: 250,
        tags: [],
        seasonality: [],
        diet: [],
        steps: [{ text: 'Смешать и приготовить.' }],
        ingredients: [{ name: ingredient, amount: 500, unit: 'g', note: null }]
      })
    })
    assert.equal(created.status, 201,
      `Создание рецепта: ${created.status} ${JSON.stringify(created.data)}`)
    const recipeId = created.data.id

    // 4. План недели и список покупок.
    const weekStart = mondayIso()
    const plan = await api(page, `/api/plans/week/${weekStart}`, {
      method: 'PUT',
      body: JSON.stringify({
        entries: [{ day: 0, mealType: 'dinner', recipeId, portions: 2 }],
        expectedRevision: 0
      })
    })
    assert.equal(plan.status, 200, `Сохранение плана: ${plan.status}`)

    const shopping = await api(page, `/api/shopping-list?weekStart=${weekStart}`)
    assert.equal(shopping.status, 200, `Список покупок: ${shopping.status}`)
    assert.ok(
      JSON.stringify(shopping.data).includes(ingredient),
      'В списке покупок нет ингредиента рецепта'
    )

    // 5. Рендер ключевых экранов.
    await page.goto(`${FRONTEND_URL}/recipes`, { waitUntil: 'domcontentloaded' })
    await page.getByText(recipeName).first().waitFor({ state: 'visible' })

    await page.goto(`${FRONTEND_URL}/plan`, { waitUntil: 'domcontentloaded' })
    await page.getByText(recipeName).first().waitFor({ state: 'visible' })

    await page.goto(`${FRONTEND_URL}/shopping`, { waitUntil: 'domcontentloaded' })
    await page.getByText(ingredient).first().waitFor({ state: 'visible' })

    // 6. Публичная ссылка: анонимный просмотр.
    const share = await api(page, `/api/recipes/${recipeId}/share`, { method: 'POST' })
    assert.ok([200, 201].includes(share.status), `Создание ссылки: ${share.status}`)
    const token1 = share.data.token
    const sharePath = share.data.path

    const anonContext = await browser.newContext()
    const anonPage = await anonContext.newPage()
    anonPage.setDefaultTimeout(STEP_TIMEOUT)
    await anonPage.goto(`${FRONTEND_URL}${sharePath}`, { waitUntil: 'domcontentloaded' })
    await anonPage.getByText('Рецепт по ссылке').waitFor({ state: 'visible' })
    await anonPage.getByText(recipeName).first().waitFor({ state: 'visible' })

    // 7. Второй пользователь добавляет внешний рецепт — проверяем состояние warning.
    const codesBefore = readCodes().length
    const emailB = `gate-smoke-b-${Date.now()}@example.com`
    const registeredB = await apiDirect('/api/auth/register', {
      method: 'POST',
      body: { email: emailB, password: PASSWORD }
    })
    assert.equal(registeredB.status, 201, `Регистрация второго пользователя: ${registeredB.status}`)
    const tokenB = registeredB.data.token
    const codeB = await waitForNewCode(codesBefore)
    const verifiedB = await apiDirect('/api/auth/verify', {
      method: 'POST', token: tokenB, body: { code: codeB }
    })
    assert.equal(verifiedB.status, 200, `Подтверждение второго пользователя: ${verifiedB.status}`)
    const familyB = await apiDirect('/api/families', {
      method: 'POST', token: tokenB, body: { name: 'Смоук-семья B' }
    })
    assert.ok([200, 201].includes(familyB.status), `Семья второго пользователя: ${familyB.status}`)
    const imported = await apiDirect(`/api/shared/${token1}/import`, { method: 'POST', token: tokenB })
    assert.ok([200, 201].includes(imported.status), `Импорт внешнего рецепта: ${imported.status}`)
    const wrapperId = imported.data.recipeId

    // Перегенерация ссылки у источника: обёртка становится «отозванной» (warning).
    const regenerated = await api(page, `/api/recipes/${recipeId}/share/regenerate`, { method: 'POST' })
    assert.ok([200, 201].includes(regenerated.status), `Перегенерация ссылки: ${regenerated.status}`)

    const bContext = await browser.newContext()
    await bContext.addInitScript(([key, value]) => {
      window.localStorage.setItem(key, value)
    }, ['menu_planner_token', tokenB])
    const bPage = await bContext.newPage()
    bPage.setDefaultTimeout(STEP_TIMEOUT)
    await bPage.goto(`${FRONTEND_URL}/recipes/${wrapperId}`, { waitUntil: 'domcontentloaded' })
    await bPage.getByText('Ссылка отозвана или перегенерирована').waitFor({ state: 'visible' })

    // 8. Удаление источника: обёртка становится «сломанной» (broken).
    const detail = await api(page, `/api/recipes/${recipeId}`)
    const removed = await api(page, `/api/recipes/${recipeId}?revision=${detail.data.revision}`, {
      method: 'DELETE'
    })
    assert.ok([200, 204].includes(removed.status), `Удаление источника: ${removed.status}`)
    await bPage.reload({ waitUntil: 'domcontentloaded' })
    await bPage.getByText('Источник удалил рецепт').waitFor({ state: 'visible' })

    // 9. Мёртвая ссылка (отозвана перегенерацией) недоступна анонимному зрителю.
    await anonPage.reload({ waitUntil: 'domcontentloaded' })
    await anonPage.getByText('Ссылка недействительна').first().waitFor({ state: 'visible' })

    await bContext.close()
    await anonContext.close()
    console.log('[browser-smoke] Все шаги пройдены')
  } finally {
    await context.close()
    await browser.close()
  }
}

main().catch((error) => {
  console.error(`[browser-smoke] ПРОВАЛ: ${error.message}`)
  process.exit(1)
})
