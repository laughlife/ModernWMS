# ModernWMS Login Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the existing Vue login page to match the approved blue logistics reference while preserving the current authentication behavior.

**Architecture:** Keep page composition and responsive layout in `login.vue`, and keep form state plus authentication flow in `login-form.vue`. Reuse the existing brand logo and Vuetify icon set, add the supplied logistics illustration as a bundled asset, and extend the existing Playwright smoke test to protect the visible contract.

**Tech Stack:** Vue 3.5, TypeScript 6, Vuetify 4, Less, Vite 8, Playwright 1.62

## Global Constraints

- Preserve the existing username/password API flow, authority loading, token storage, remembered credentials, password visibility toggle, Enter submission, messages, and home redirect.
- Use `frontend/src/assets/img/logo.png` for branding.
- Copy `D:\Users\Administrator\Desktop\nyamtn.png` into the frontend asset directory for the left illustration.
- Do not add or display SSO, registration, or alternative authentication controls.
- Desktop uses the approved two-column layout; narrow screens use a single login-card layout without horizontal overflow.

---

### Task 1: Protect the Login Page Contract

**Files:**
- Modify: `frontend/tests/smoke/login.spec.ts`

**Interfaces:**
- Consumes: the `/login` Vue route and rendered login form.
- Produces: Playwright assertions for branding, illustration, form controls, and absence of SSO.

- [ ] **Step 1: Extend the smoke test with the approved visible contract**

```ts
test('login page presents the approved logistics layout', async ({ page }) => {
  await page.goto('/#/login')

  await expect(page.getByRole('heading', { name: '南阳有座山物流管理系统' })).toBeVisible()
  await expect(page.getByRole('heading', { name: '欢迎登录' })).toBeVisible()
  await expect(page.getByTestId('login-hero-image')).toBeVisible()
  await expect(page.getByTestId('login-submit')).toBeVisible()
  await expect(page.getByText('SSO单点登录')).toHaveCount(0)
})
```

- [ ] **Step 2: Run the new test and verify it fails against the old page**

Run: `npm run test:e2e -- --grep "approved logistics layout"`

Expected: FAIL because the approved heading, test IDs, and structure do not exist yet.

- [ ] **Step 3: Commit the failing UI contract test**

```powershell
git add frontend/tests/smoke/login.spec.ts
git commit -m "测试：补充登录页视觉结构契约"
```

### Task 2: Build the Branded Responsive Shell

**Files:**
- Create: `frontend/src/assets/img/login-warehouse.png`
- Modify: `frontend/src/view/login/login.vue`

**Interfaces:**
- Consumes: `Logo`, `LoginForm`, the supplied `nyamtn.png`, and existing ICP click behavior.
- Produces: `.login-page`, `.brand-bar`, `.login-main`, `.hero-panel`, `.login-panel`, `.capability-strip`, and responsive breakpoints consumed only by this page.

- [ ] **Step 1: Copy the supplied illustration into the project**

Run: `Copy-Item -LiteralPath 'D:\Users\Administrator\Desktop\nyamtn.png' -Destination 'frontend\src\assets\img\login-warehouse.png' -Force`

Expected: destination SHA-256 equals the source SHA-256.

- [ ] **Step 2: Replace the old 70/30 shell with semantic page sections**

Implement a template with this hierarchy:

```vue
<div class="login-page">
  <header class="brand-bar">...</header>
  <main class="login-main">
    <section class="hero-panel">
      <h1>南阳有座山物流管理系统</h1>
      <p>高效 · 智能 · 协同 · 安全</p>
      <img data-testid="login-hero-image" src="@/assets/img/login-warehouse.png" alt="仓储物流管理场景" />
      <div class="capability-strip">...</div>
    </section>
    <section class="login-panel"><LoginForm /></section>
  </main>
  <footer class="login-footer">...</footer>
</div>
```

Use the existing `Logo` component in the brand bar, add the brand text beside it, and render the four capability entries with `v-icon` names `mdi-package-variant-closed`, `mdi-truck-outline`, `mdi-clipboard-text-outline`, and `mdi-chart-pie`.

- [ ] **Step 3: Add responsive Less styling**

Use a light blue page background, a white 96px brand bar, a centered main area with a maximum width near 1500px, a white card with 14px radius and subtle shadow, and media queries near 1100px and 760px. At 760px hide `.hero-panel`, keep the brand bar compact, and make `.login-panel` fill the available width.

- [ ] **Step 4: Verify the illustration and responsive shell**

Run: `npm run build`

Expected: PASS with no Vue or TypeScript errors.

- [ ] **Step 5: Commit the page shell**

```powershell
git add frontend/src/assets/img/login-warehouse.png frontend/src/view/login/login.vue
git commit -m "功能：重构登录页品牌主视觉布局"
```

### Task 3: Restyle the Existing Login Form Without Changing Behavior

**Files:**
- Modify: `frontend/src/components/login/login-form.vue`

**Interfaces:**
- Consumes: current `data` state, `method.login`, Vuetify form components, i18n labels, and existing stores/API calls.
- Produces: a styled login card with the same functional behavior and `data-testid="login-submit"` for smoke verification.

- [ ] **Step 1: Reshape only the form presentation**

Add a subtitle `南阳有座山物流管理系统`, attach `data-testid="login-submit"` to the existing submit button, use a blue button color, preserve both text fields and the remember checkbox, and leave the complete script logic unchanged.

- [ ] **Step 2: Implement card-aligned form styling**

Set a centered title, muted subtitle, outlined field surfaces, a 52px blue button, clear focus states, and compact mobile spacing. Use scoped `:deep(...)` selectors only for the Vuetify internals that cannot be styled through component props.

- [ ] **Step 3: Run the focused Playwright test**

Run: `npm run test:e2e -- --grep "login page"`

Expected: PASS for both the original input test and the approved logistics layout test.

- [ ] **Step 4: Commit the form redesign**

```powershell
git add frontend/src/components/login/login-form.vue
git commit -m "功能：更新登录表单视觉样式"
```

### Task 4: Complete Visual and Regression Verification

**Files:**
- Create: `design-qa.md`
- Modify if required by QA: `frontend/src/view/login/login.vue`
- Modify if required by QA: `frontend/src/components/login/login-form.vue`

**Interfaces:**
- Consumes: the provided reference screenshot and locally rendered `/login` page.
- Produces: a passing QA record plus verified desktop and narrow-screen behavior.

- [ ] **Step 1: Run automated verification**

Run: `npm run test:unit`

Expected: PASS.

Run: `npm run build`

Expected: PASS.

Run: `npm run test:e2e -- --grep "login page"`

Expected: PASS.

- [ ] **Step 2: Capture the desktop implementation**

Open `http://localhost:5173/#/login` in the Codex in-app browser at the same wide viewport as the reference. Verify the logo, headline, illustration, capabilities, form card, footer, and absence of SSO.

- [ ] **Step 3: Capture and inspect the narrow layout**

Use a 390×844 viewport. Verify there is no horizontal scroll, the form remains fully visible, and all inputs and the submit button are usable.

- [ ] **Step 4: Record and fix design QA findings**

Create `design-qa.md` with comparison scope, findings by severity, fixes applied, remaining P3 notes, and the exact line `final result: passed`. If any P0/P1/P2 issue exists, fix it and repeat desktop plus narrow capture before marking passed.

- [ ] **Step 5: Commit the verified implementation**

```powershell
git add frontend/src/view/login/login.vue frontend/src/components/login/login-form.vue design-qa.md
git commit -m "验证：完成登录页视觉与响应式验收"
```
