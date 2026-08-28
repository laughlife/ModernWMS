# Actual Packing and Negative Stock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade `wms_weighing_box_item` in place so box-level actual inventory is authoritative, and allow packing/outbound to create and consume negative inventory without blocking the workflow.

**Architecture:** `wms_dispatch_packing_task_item` remains the plan/reference. `wms_weighing_box_item` becomes the single box-level actual fact and carries the exact ERP stock allocation. Confirmation replaces planned dispatch allocations with deterministic allocations derived from actual lines; stock reservations remain nonnegative while stock balances/allocation balances may be negative.

**Tech Stack:** .NET 10, ASP.NET Core, Dapper, MySqlConnector, MySQL 8, Flyway 11.15.0, Vue 3, TypeScript 6, Vitest.

**Spec:** `docs/plans/2026-08-28-actual-packing-and-negative-stock-design.md`

## Global Constraints

- Do not create a new table.
- Upgrade only `wms_weighing_box_item` for actual box contents.
- Packing quantity, SKU and owner may differ from the task plan.
- Insufficient inventory is visible but never blocks packing, confirmation or outbound.
- `SHIP_OUT` is irreversible; intercepted returns are separate inbound records.
- WMS must not call Ruoyi/ERP HTTP APIs.
- Apply Flyway only after `DATABASE()` is exactly `ruoyi-vue-pro`; never connect to production.
- Preserve and do not stage unrelated dirty worktree files.

---

### Task 1: Forward migration for the existing box-item table

**Files:**
- Create: `flyway/sql/V20260828120000__actual_packing_and_negative_stock.sql`
- Create: `backend/ModernWMS.Tests/Database/ActualPackingMigrationMySqlIntegrationTests.cs`

**Interfaces:**
- Consumes: existing `wms_weighing_box_item`, allocation and allocation-log tables.
- Produces: upgraded columns `client_line_key`, nullable `packing_task_item_id`, `wms_sku_id`, `erp_stock_id`, `stock_allocation_id`, inventory snapshots, `actual_qty`, nullable `dispatchpicklist_id`.

- [ ] **Step 1: Write the failing integration test**

Create a development-DB-gated test which reads `INTEGRATION_MYSQL_CONNECTION`, executes Flyway externally only when explicitly enabled, and then asserts from `information_schema` that no `wms_weighing_box_inventory_item` exists, the upgraded columns exist, and the negative-allocation checks were removed.

```csharp
Assert.False(await TableExistsAsync(connection, "wms_weighing_box_inventory_item"));
Assert.True(await ColumnExistsAsync(connection, "wms_weighing_box_item", "actual_qty"));
Assert.False(await CheckExistsAsync(connection, "ck_erp_stock_allocation_allocated_nonnegative"));
```

- [ ] **Step 2: Run the test and verify RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~ActualPackingMigrationMySqlIntegrationTests`

Expected: skipped without explicit integration configuration; with the authorized development connection it fails because `actual_qty` does not exist.

- [ ] **Step 3: Write the forward migration**

The migration must:

```sql
ALTER TABLE `wms_weighing_box_item`
  DROP INDEX `uk_weighing_box_item`,
  MODIFY `packing_task_item_id` int NULL,
  CHANGE COLUMN `task_qty` `actual_qty` int NOT NULL,
  ADD COLUMN `client_line_key` varchar(64) NOT NULL AFTER `weighing_box_id`,
  ADD COLUMN `wms_sku_id` int NOT NULL AFTER `packing_task_item_id`,
  ADD COLUMN `erp_stock_id` bigint NOT NULL AFTER `wms_sku_id`,
  ADD COLUMN `stock_allocation_id` bigint NOT NULL AFTER `erp_stock_id`,
  ADD COLUMN `goods_owner_id` int NOT NULL AFTER `stock_allocation_id`,
  ADD COLUMN `goods_location_id` int NOT NULL AFTER `goods_owner_id`,
  ADD COLUMN `sku_code` varchar(255) NOT NULL AFTER `goods_location_id`,
  ADD COLUMN `commodity_name` varchar(500) NOT NULL AFTER `sku_code`,
  ADD COLUMN `dispatchpicklist_id` int NULL AFTER `actual_qty`;
```

Because development rows are disposable, delete existing `wms_weighing_box_item` rows before introducing required actual-inventory columns. Add the actual-quantity check, deterministic unique key and indexes. Drop only the four incompatible allocation-log checks and the two incompatible allocation checks; retain occupied nonnegative and location-state checks.

- [ ] **Step 4: Validate migration syntax without applying**

Run: `pwsh scripts/Update-Database.ps1 -ConfirmDevelopmentDatabase`

Expected: Flyway `info` and `validate` succeed; no migrate command is issued.

- [ ] **Step 5: Commit the migration slice**

```bash
git add flyway/sql/V20260828120000__actual_packing_and_negative_stock.sql backend/ModernWMS.Tests/Database/ActualPackingMigrationMySqlIntegrationTests.cs
git commit -m "feat: 升级实际装箱明细表结构"
```

### Task 2: Negative-stock invariant policy

**Files:**
- Create: `backend/ModernWMS.Tests/StockAllocation/StockBalanceInvariantTests.cs`
- Create: `backend/ModernWMS.WMS/Services/StockAllocation/StockBalanceInvariant.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.CanonicalInventory.cs`

**Interfaces:**
- Produces: `StockBalanceInvariant.EnsureValid(long available, long occupied, long total, long allocated, long allocationOccupied)`.

- [ ] **Step 1: Write failing policy tests**

```csharp
[Fact]
public void Allows_negative_available_total_and_allocation_when_conservation_holds() =>
    StockBalanceInvariant.EnsureValid(-20, 0, -20, -20, 0);

[Theory]
[InlineData(0, -1, -1, 0, 0)]
[InlineData(0, 0, 1, 0, 0)]
[InlineData(0, 0, 0, 0, -1)]
public void Rejects_negative_occupied_or_broken_stock_conservation(
    long available, long occupied, long total, long allocated, long allocationOccupied)
{
    Assert.Throws<InvalidOperationException>(() =>
        StockBalanceInvariant.EnsureValid(
            available, occupied, total, allocated, allocationOccupied));
}
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~StockBalanceInvariantTests`

Expected: compile failure because `StockBalanceInvariant` is missing.

- [ ] **Step 3: Implement the policy and replace old nonnegative guards**

```csharp
if (occupied < 0 || allocationOccupied < 0)
    throw new InvalidOperationException("预占数量不能为负数");
if (total != checked(available + occupied))
    throw new InvalidOperationException("ERP库存三分量不守恒");
```

Do not reject negative `available`, `total`, `allocated`, or `allocationOccupied > allocated`. Reuse the same policy in receipt invariant verification so later inbound can settle a negative balance.

- [ ] **Step 4: Run targeted and existing allocation tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter 'FullyQualifiedName~StockBalanceInvariantTests|FullyQualifiedName~StockAllocationInvariantMaterializationTests'`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.Tests/StockAllocation/StockBalanceInvariantTests.cs backend/ModernWMS.WMS/Services/StockAllocation/StockBalanceInvariant.cs backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.CanonicalInventory.cs
git commit -m "feat: 允许库存欠账并保持数量守恒"
```

### Task 3: Actual-line API contract and persistence

**Files:**
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowEntities.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow/DispatchWorkflowViewModels.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs`
- Create: `backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingLinePolicyTests.cs`

**Interfaces:**
- `PackingPlanBoxItemViewModel`: `client_line_key`, nullable `packing_task_item_id`, `stock_allocation_id`, `actual_qty` plus server-owned inventory snapshots.
- `GetActualPackingStockAsync(orderId, taskId, keyword, user, ct)`: returns active allocations in the order warehouse, including zero/negative free quantity and all owners.

- [ ] **Step 1: Write failing tests for line validation**

Cover a task-linked line, task-external line, different-SKU line, other-owner line, duplicate line key, nonpositive quantity and an allocation from another warehouse. The first four must pass; the last three must fail.

- [ ] **Step 2: Verify RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~ActualPackingLinePolicyTests`

Expected: compile failure because the actual-line contract is missing.

- [ ] **Step 3: Implement DTOs, entity mapping and inventory query**

The query joins `wms_erp_stock_allocation -> trk_stock -> wms_erp_commodity_map -> wms_sku -> wms_spu`, restricts the ERP warehouse and `location_state='ACTIVE'`, but does not filter owner, SKU or free quantity.

- [ ] **Step 4: Persist only server-verified inventory identity**

`SavePackingPlanAsync` locks all submitted allocation IDs, verifies warehouse and active locations, deletes/reinserts box lines, and writes snapshots from database rows. Never trust SKU, owner, location or ERP stock IDs from the browser.

- [ ] **Step 5: Run endpoint and policy tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter 'FullyQualifiedName~ActualPackingLinePolicyTests|FullyQualifiedName~DispatchWorkflowEndpointTests'`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/ModernWMS.WMS/Entities backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingLinePolicyTests.cs
git commit -m "feat: 保存箱内实际库存明细"
```

### Task 4: Materialize actual lines into dispatch reservations

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/ActualPackingMaterializationPolicy.cs`
- Create: `backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingMaterializationPolicyTests.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs`

**Interfaces:**
- `ActualPackingMaterializationPolicy.Build(currentPicks, actualLines)` returns deterministic releases and reserves ordered by ERP stock/allocation/business line.

- [ ] **Step 1: Write failing planner tests**

Use literal vectors for less/equal/more, changed allocation, task-external SKU and idempotent replay. Assert exact release/reserve quantities rather than mock calls.

- [ ] **Step 2: Verify RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~ActualPackingMaterializationPolicyTests`

Expected: compile failure because the planner is missing.

- [ ] **Step 3: Implement deterministic planner**

Group actual lines by nullable task item, SKU, ERP stock and allocation. Release old pick quantities not retained and reserve target quantities using keys derived from task ID, request ID and actual line key; never use timestamps or counts.

- [ ] **Step 4: Integrate confirmation transaction**

Confirm actual packing must release old unconsumed picks, recreate/update dispatch details and picks from actual lines, reserve any deficit even when stock becomes negative, write `dispatchpicklist_id` to actual lines, and mark the task `ACTUAL_CONFIRMED` in one transaction.

- [ ] **Step 5: Permit task-external details in outbound ownership validation**

When `packing_task_item_id` is null, validate ownership through `wms_weighing_box_item.dispatchpicklist_id`, its box and packing task. Keep the existing validation for planned task items.

- [ ] **Step 6: Run dispatch tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter 'FullyQualifiedName~ActualPackingMaterializationPolicyTests|FullyQualifiedName~DispatchWorkflow'`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingMaterializationPolicyTests.cs
git commit -m "feat: 按实际装箱结算出库预占"
```

### Task 5: Frontend actual-inventory editor

**Files:**
- Modify: `frontend/src/types/DeliveryManagement/DispatchWorkflow.ts`
- Modify: `frontend/src/api/wms/dispatchWorkflow.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.spec.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanCompletion.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.spec.ts`

**Interfaces:**
- Each box line edits a selected inventory allocation and `actual_qty`; optional `packing_task_item_id` only labels the plan reference.

- [ ] **Step 1: Write failing policy tests**

Assert that over-plan quantities, task-external lines and other-owner lines are valid; zero/negative actual quantities and duplicate client keys are invalid. Assert that inspection reports negative stock as a warning, not an issue.

- [ ] **Step 2: Verify RED**

Run: `npm run test:unit -- packingPlanPolicy.spec.ts packing-task-weighing-editor.spec.ts`

Expected: FAIL because the old model caps task quantities and has no inventory line identity.

- [ ] **Step 3: Update types/API and editor behavior**

Add an inventory selector dialog/search, show SKU/product/owner/location/current available quantity, allow any returned inventory row, allow extra rows, and label negative projected availability as a warning. Remove plan-equality and plan-ceiling blockers.

- [ ] **Step 4: Run frontend tests and build**

Run: `npm run test:unit -- packingPlanPolicy.spec.ts packing-task-weighing-editor.spec.ts dispatchWorkflow.spec.ts`

Run: `npm run build`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types/DeliveryManagement/DispatchWorkflow.ts frontend/src/api/wms/dispatchWorkflow.ts frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.ts frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.spec.ts frontend/src/view/deliveryManagement/deliveryManagement/packingPlanCompletion.ts frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.vue frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.spec.ts
git commit -m "feat: 以实际库存编辑装箱内容"
```

### Task 6: Apply to the authorized development database and verify

**Files:**
- Modify: `docs/plans/2026-08-28-actual-packing-and-negative-stock-design.md` only if executed evidence changes its status.

**Interfaces:**
- Consumes: development connection already configured for this repository.
- Produces: migrated `ruoyi-vue-pro` development schema and verification evidence.

- [ ] **Step 1: Prove the database identity**

Run a read-only query first:

```sql
SELECT DATABASE();
```

Expected literal result: `ruoyi-vue-pro`. Stop if it differs.

- [ ] **Step 2: Apply Flyway once**

Run: `pwsh scripts/Update-Database.ps1 -ConfirmDevelopmentDatabase -Apply`

Expected sequence: `info`, `validate`, `migrate`; migration version `20260828120000` succeeds once.

- [ ] **Step 3: Run real schema assertions**

Run the gated `ActualPackingMigrationMySqlIntegrationTests` and direct `information_schema` queries. Confirm the old table was upgraded, no new actual-packing table exists, and only intended checks changed.

- [ ] **Step 4: Run full relevant regression**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --no-restore`

Run: `npm run test`

Expected: PASS.

- [ ] **Step 5: Commit final design status/evidence if changed**

```bash
git add docs/plans/2026-08-28-actual-packing-and-negative-stock-design.md
git commit -m "docs: 记录实际装箱迁移验证结果"
```

- [ ] **Step 6: Stop for user acceptance**

Report migrations, commits, tests and manual test steps. Do not start the next contract task until the user reports this flow passed.
