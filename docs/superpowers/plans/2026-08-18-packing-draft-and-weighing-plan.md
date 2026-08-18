# 装箱草稿与任务量称重 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为每条独立装箱任务提供可保存草稿、同任务商品混装、按任务量分箱、确认实际数量释放余量、完成称重后进入待出库的闭环。

**Architecture:** 使用 `wms_weighing_box` 保存 WMS 自建实体箱，新增 `wms_weighing_box_item` 保存箱内任务量；装箱任务和商品行追加方案状态、创建时变体快照及实际装箱数量。任务级完整草稿接口原子替换箱方案，确认实际接口原子缩减拣货库存分配，完成接口在后端重新验证箱规、重量、商品数量和库存一致性。

**Tech Stack:** ASP.NET Core、Dapper、MySQL/Flyway、Vue 3 `<script setup>`、TypeScript、Vuetify。

**Spec:** `docs/superpowers/specs/2026-08-18-packing-draft-and-weighing-design.md`

## Global Constraints

- 一条装箱任务独立装箱，不同任务绝不混装。
- 箱内唯一可编辑商品数量是任务量；`variant_qty` 只读，商品件数自动计算。
- 保存草稿不释放库存；只有确认实际装箱才释放余量。
- 不启动、停止或重启开发服务，不执行数据库迁移，不运行测试或构建，只做静态验收。
- 迁移只追加 `wms_` 表和字段，不修改历史 FBA 表。
- 保护当前工作区已有未提交修改，每个提交只暂存本计划文件。

---

### Task 1: 追加装箱方案数据库契约

**Files:**
- Create: `flyway/sql/V3__packing_draft_and_weighing_plan.sql`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowEntities.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowOperationEntity.cs`

**Interfaces:**
- Produces: `DispatchPackingPlanStatus`、`DispatchPackingTaskItemEntity.variant_qty`、`WeighingBoxItemEntity`，供后端服务加载和保存。

- [ ] **Step 1: 创建仅追加迁移**

迁移追加任务状态、商品变体/实际数量字段和箱内商品表，核心约束：

```sql
ALTER TABLE `wms_dispatch_packing_task`
  ADD COLUMN `packing_plan_status` varchar(24) NOT NULL DEFAULT 'DRAFT';
ALTER TABLE `wms_dispatch_packing_task_item`
  ADD COLUMN `variant_qty` int NULL,
  ADD COLUMN `actual_packed_task_qty` int NULL,
  ADD COLUMN `actual_packed_required_qty` int NULL;
CREATE TABLE `wms_weighing_box_item` (
  `id` int NOT NULL AUTO_INCREMENT,
  `weighing_box_id` int NOT NULL,
  `packing_task_item_id` int NOT NULL,
  `task_qty` int NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_weighing_box_item` (`weighing_box_id`,`packing_task_item_id`)
);
```

迁移对现有有效商品按 `required_qty / source_quantity_shipped` 回填正整数变体；无法回填的值保持 `NULL`，由业务层拦截。

- [ ] **Step 2: 映射实体与操作类型**

新增 `SavePackingDraft=32`、`ConfirmActualPacking=35` 操作账本枚举，任务实体增加方案状态和审计字段，箱实体增加 `List<WeighingBoxItemEntity> items`。

- [ ] **Step 3: 静态检查并提交**

Run: `git diff --check -- flyway/sql/V3__packing_draft_and_weighing_plan.sql backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowEntities.cs backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowOperationEntity.cs`

Commit: `数据库：追加装箱草稿与箱内商品模型`

### Task 2: 固化创建时变体并提供任务级 DTO/API

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Creation.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow/DispatchWorkflowViewModels.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`

**Interfaces:**
- Produces: `PackingPlanViewModel GetPackingPlanAsync(...)`、`SavePackingPlanAsync(...)`、`ConfirmActualPackingAsync(...)`。

- [ ] **Step 1: 创建时固化变体**

`CreateItem` 使用已绑定库存数量与来源任务量验证并保存：

```csharp
variant_qty = requiredQty > 0 && item.Quantity > 0 && requiredQty % item.Quantity == 0
    ? requiredQty / item.Quantity
    : null;
```

无正整数变体时拒绝创建拣货单，不延迟到称重阶段猜测。

- [ ] **Step 2: 定义草稿快照 DTO**

DTO 包含任务商品池、`variant_qty`、计划/实际任务量、箱、箱内商品、任务和订单版本。保存请求采用完整箱快照：`request_id`、`row_version`、`task_row_version`、`boxes[]`。

- [ ] **Step 3: 暴露三个任务级端点**

```text
GET  /dispatch-workflow/{id}/packing-tasks/{packingTaskId}/packing-plan
PUT  /dispatch-workflow/{id}/packing-tasks/{packingTaskId}/packing-plan
POST /dispatch-workflow/{id}/packing-tasks/{packingTaskId}/confirm-actual
```

- [ ] **Step 4: 静态检查并提交**

Run: `git diff --check -- backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Creation.cs backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow/DispatchWorkflowViewModels.cs backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`

Commit: `装箱任务：固化变体并增加草稿接口契约`

### Task 3: 实现草稿保存与实际数量确认事务

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Weighing.cs`

**Interfaces:**
- Consumes: Task 1 实体和 Task 2 DTO。
- Produces: 完整草稿替换、实际数量确认、余量释放和幂等结果。

- [ ] **Step 1: 加载任务级装箱方案**

一次读取订单、任务、有效商品、有效箱和箱内商品；返回只读变体和自动计算的商品件数。

- [ ] **Step 2: 保存完整草稿**

串行化事务锁定聚合，验证 `WEIGHING`、来源未冻结、版本一致、商品/箱同任务、任务量正整数且不超上限。按客户端临时箱键 upsert 箱，替换箱内商品，对删除箱逻辑作废；不更新 `wms_dispatchlist` 或 `wms_dispatchpicklist`。

- [ ] **Step 3: 确认实际装箱并释放余量**

按 `packed_task_qty × variant_qty` 计算实际商品件数，按分配记录倒序缩减 `wms_dispatchpicklist.pick_qty/picked_qty`，同步缩减对应 `wms_dispatchlist.qty/lock_qty/picked_qty`，写入实际数量和审计。释放总量不一致时回滚。

- [ ] **Step 4: 调整进入称重和完成校验**

`StartWeighingAsync` 不再依赖来源箱能力，不预建 SellFox 箱；任务进入 `WEIGHING/DRAFT`。完成任务时重新验证每个有效箱有商品、四项测量完整、各商品箱内任务量等于允许量、库存分配等于实际商品件数。

- [ ] **Step 5: 静态检查并提交**

Run: `git diff --check -- backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Weighing.cs`

Commit: `称重流程：支持装箱草稿与实际余量释放`

### Task 4: 增加前端类型、API 和草稿状态工具

**Files:**
- Modify: `frontend/src/types/DeliveryManagement/DispatchWorkflow.ts`
- Modify: `frontend/src/api/wms/dispatchWorkflow.ts`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.ts`

**Interfaces:**
- Produces: `PackingPlan`、`SavePackingPlanRequest`、`get/save/confirmDispatchPackingPlan`，以及任务量汇总和完成条件纯函数。

- [ ] **Step 1: 对齐前端契约**

定义与后端一致的商品池、箱、箱内商品、草稿状态和版本字段；变体和商品件数为只读展示字段。

- [ ] **Step 2: 增加三个 API 方法**

使用 Task 2 的三个路由，保存和确认返回最新 `PackingPlanViewModel`，避免另发请求覆盖本地草稿。

- [ ] **Step 3: 实现任务量计算工具**

提供逐商品已分配任务量、剩余任务量、箱内商品件数和 `canCompletePackingPlan`；所有输入上限只比较任务量。

- [ ] **Step 4: 静态检查并提交**

Run: `git diff --check -- frontend/src/types/DeliveryManagement/DispatchWorkflow.ts frontend/src/api/wms/dispatchWorkflow.ts frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.ts`

Commit: `发货管理：增加装箱草稿前端契约`

### Task 5: 实现称重一级页面装箱编辑器

**Files:**
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabWeighed.vue`
- Remove: new-page usage of `frontend/src/view/deliveryManagement/deliveryManagement/shipment-box-weigh-dialog.vue` while retaining file for compatibility.

**Interfaces:**
- Consumes: Task 4 API 和策略函数。
- Produces: 商品池、箱卡片、保存草稿、确认实际、称重完成交互。

- [ ] **Step 1: 展示完整商品池**

每行展示图片、商品/SKU、FNSKU/MSKU、变体、任务量、自动商品需求量、已分配/剩余任务量及“建立装箱并称重”。

- [ ] **Step 2: 实现箱卡片编辑**

支持新增空箱、从商品行创建箱、同任务商品选择、任务量输入、删除商品、删除箱、重量和长宽高输入。不得出现可编辑变体或商品件数字段。

- [ ] **Step 3: 实现三个动作**

保存草稿允许未分完和测量不完整；确认实际弹窗逐项列出未装任务量与自动换算的释放件数；完成按钮只在前端完整条件满足时可点，并处理后端最终校验错误。

- [ ] **Step 4: 集成一级页面**

`tabWeighed.vue` 的每条装箱任务直接挂载编辑器，不再显示“功能待确认”提示；操作成功刷新角标和当前行。

- [ ] **Step 5: Vue 静态解析并提交**

Run: 使用 `@vue/compiler-sfc` 解析 `packing-task-weighing-editor.vue` 与 `tabWeighed.vue`，再执行 `git diff --check`。

Commit: `称重页面：实现任务量装箱草稿编辑`

### Task 6: 全链路静态验收

**Files:**
- Review: 本计划所有变更文件。

**Interfaces:**
- Produces: 静态验收结果和未执行事项清单。

- [ ] **Step 1: 契约逐项核对**

确认路由、DTO 字段、状态、错误码一致；确认每个箱和箱内商品都强制归属同一任务。

- [ ] **Step 2: 数量与库存路径核对**

确认页面只编辑任务量，变体来自创建快照，商品件数自动计算；草稿路径没有库存 SQL，确认实际路径在单事务缩减分配。

- [ ] **Step 3: 状态路径核对**

确认草稿保持 `WEIGHING`，实际确认不提前进入下一阶段，只有完整称重完成才进入 `PENDING_OUTBOUND`。

- [ ] **Step 4: 最终静态命令**

Run: `git diff --check`，Vue SFC 静态解析，`rg` 检查新路由/字段引用。按用户要求不运行测试、构建、服务或数据库迁移。

- [ ] **Step 5: 提交剩余整合改动**

Commit: `发货管理：完成装箱草稿称重闭环`
