# 已出库明细展示优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让已出库列表默认展开，并以白底、中文、按箱序和箱内商品组织的表格展示已出库装箱明细。

**Architecture:** 保持已出库页面现有主单、装箱任务、物理箱三级结构。展示格式和箱内商品关联逻辑放入 `completedOutboundPolicy.ts`，页面组件只负责加载、自动展开和渲染，避免在模板中重复查找及拼接规则。

**Tech Stack:** Vue 3 `<script setup>`、TypeScript、Vuetify、vxe-table、Vitest。

**Spec:** `C:\Users\ADMINI~1\AppData\Local\Temp\codex-clipboard-fedb67e8-b8f2-40da-93b4-ed1e916e7817.png` 及当前任务中的 5 条中文需求。

## Global Constraints

- 已出库行加载后默认展开并加载明细。
- 明细区域和箱表使用白色背景。
- 物理箱显示为 `箱1`、`箱2`，不得显示内部来源标识。
- 箱表按图片、商品信息、重量、尺寸和中文测量状态展示，尺寸格式为长 × 宽 × 高。
- 不修改后端接口，不覆盖工作区其他已有改动。

---

### Task 1: 固化已出库箱展示规则

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`

**Interfaces:**
- Consumes: `DispatchPackingTaskItem`、`PackingPlanBoxItem` 和 `WeighingBox`。
- Produces: 物理箱名称、尺寸、中文测量状态和箱内商品解析函数。

- [x] **Step 1: Write the failing test**

  增加断言：箱序 1 显示 `箱1`；15、20、20 显示 `15 × 20 × 20`；`MEASURED` 显示 `已测量`；箱内条目能关联商品图片、名称和 SKU。

- [x] **Step 2: Run test to verify it fails**

  Run: `npm.cmd run test:unit -- src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`
  Expected: FAIL，因为展示函数尚不存在。

- [x] **Step 3: Write minimal implementation**

  在 policy 中实现纯函数并保留接口返回的箱内 `items`。

- [x] **Step 4: Run test to verify it passes**

  Run: `npm.cmd run test:unit -- src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`
  Expected: PASS。

### Task 2: 重构已出库明细表并默认展开

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabCompleted.vue`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`

**Interfaces:**
- Consumes: Task 1 的已出库箱展示函数。
- Produces: 默认展开、白底、箱内商品两列、合并尺寸列和中文测量状态的页面。

- [x] **Step 1: Write the failing test**

  用组件加载规则验证页面数据加载后调用 `setAllRowExpand(true)`；其余展示由 Task 1 的纯函数测试覆盖。

- [x] **Step 2: Run test to verify it fails**

  Run: `npm.cmd run test:unit -- src/view/deliveryManagement/deliveryManagement/tabCompleted.spec.ts`
  Expected: FAIL，因为页面尚未自动展开。

- [x] **Step 3: Write minimal implementation**

  配置 expandAll，数据写入后等待 DOM 更新并展开所有行，同时加载每行详情；将箱表改为 `物理箱、箱序、图片、商品信息、重量(kg)、尺寸(cm)、测量状态`，并将明细背景设为白色。

- [x] **Step 4: Run test to verify it passes**

  Run: `npm.cmd run test:unit -- src/view/deliveryManagement/deliveryManagement/tabCompleted.spec.ts`
  Expected: PASS。

### Task 3: 全量验证并提交

**Files:**
- Verify only the files listed above and this plan.

**Interfaces:**
- Consumes: Tasks 1-2 的实现。
- Produces: 可构建、可回归的已出库明细改造。

- [x] **Step 1: Run all frontend tests**

  Run: `npm.cmd run test:unit`
  Expected: 全部通过。

- [x] **Step 2: Run production build**

  Run: `npm.cmd run build`
  Expected: 构建退出码 0；允许项目现有第三方 eval 和 chunk-size 警告。

- [ ] **Step 3: Review task-only diff**

  Run: `git diff --check -- <本计划涉及文件>`
  Expected: 无空白错误，暂存区仅包含本任务文件。

- [ ] **Step 4: Commit**

  Run: `git commit -m "已出库：优化装箱明细展示"`
