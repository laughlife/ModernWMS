# Packing Task Dispatch Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a warehouse-authorized workflow in which multiple same-warehouse SellFox packing tasks create one WMS picking order and move atomically through picking, weighing, pending outbound, and outbound.

**Architecture:** XXL-maintained SellFox tables stay read-only; all mutable workflow facts live in additive `wms_` tables. A true WMS master order owns task snapshots, task-scoped products, stock allocations, source-change adjudication, and physical-box measurements. The first tab is task-based; the other five tabs are order-based.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10/MySQL, xUnit, Vue 3 `<script setup lang="ts">`, TypeScript 6, Vuetify/VXE Table, Vitest, Playwright.

## Global Constraints

- ModernWMS never calls SellFox HTTP; source reads use only XXL-maintained shared tables.
- One order may contain multiple tasks, all with one ERP warehouse ID. Equal SKUs in different tasks never merge.
- Creation performs no stock work. Picking completion reconciles and allocates the whole order atomically; shortage leaves `PENDING_PICK` and zero partial allocations.
- Printing reconciles and returns the current fully expanded snapshot but never changes status.
- Admin sees all valid warehouses and defaults to `320118`; ordinary roles see only explicit role-warehouse bindings. Tenant ID does not decide workflow visibility.
- WMS measurements are authoritative. Boxes must already exist by stable identity in the current `cartons_json`; copy updates an existing same-task target and never creates a box.
- A new source change after `PICKED` freezes the order until a human chooses continue or cancel.
- SellFox weight/dimension write-back is explicitly excluded from this release; future write-back is once per fully measured task, never once per box.
- Historical FBA/dispatch facts are not converted. New workflow writes and legacy compatibility reads remain separate.
- Tasks 1-9 own all shared backend/API/type/i18n files. Tasks 10-15 may edit only their explicitly listed page and pure-function files.

---

### Task 1: Verify source fields and stable physical-box identity

**Files:**
- Modify: `backend/ModernWMS.Core/DBContext/Entities/ErpPackingTaskEntities.cs`
- Create: `backend/ModernWMS.WMS/Entities/ViewModels/PackingTask/PackingTaskSourceContracts.cs`
- Create: `backend/ModernWMS.WMS/IServices/PackingTask/IPackingTaskSourceReader.cs`
- Create: `backend/ModernWMS.WMS/Services/PackingTask/SellFoxCartonParser.cs`
- Create: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskSourceReader.cs`
- Test: `backend/ModernWMS.Tests/PackingTask/SellFoxCartonParserTests.cs`
- Test: `backend/ModernWMS.Tests/PackingTask/PackingTaskSourceReaderTests.cs`

**Interfaces:**
- Consumes: `ruiyi_sellfox_packing_task`, `ruiyi_sellfox_packing_task_item`, required `cartons_json`, `sellfox_task_id`, and `sellfox_item_id`.
- Produces: `IPackingTaskSourceReader.VerifyCapabilityAsync(CancellationToken)` and `ReadAsync(IReadOnlyCollection<long>, CancellationToken)`.
- Produces: `PackingTaskSourceSnapshot(long SourceTaskId, string TaskNo, long WarehouseId, string WarehouseName, string SourceVersion, bool IsCancelled, IReadOnlyList<PackingTaskSourceItem> Items, IReadOnlyList<SellFoxSourceBox> Boxes, string CartonsJson)`.
- Produces: `SellFoxSourceBox(string SourceBoxIdentity, int Sequence, string SourceSnapshot)`. Identity keys are checked in this order: `boxId`, `box_id`, `cartonId`, `carton_id`, `id`. Missing/blank/duplicate identities fail closed; array position is never a stable ID.

- [ ] **Step 1: Write failing parser/source tests**

```csharp
[Fact]
public void Parse_rejects_array_index_as_identity()
{
    var result = SellFoxCartonParser.Parse("[{\"weight\":1},{\"weight\":2}]");
    Assert.False(result.IsSupported);
    Assert.Contains("稳定箱ID", result.Error);
}

[Fact]
public void Parse_preserves_unique_source_ids()
{
    var result = SellFoxCartonParser.Parse("[{\"cartonId\":\"C-2\"},{\"cartonId\":\"C-1\"}]");
    Assert.Equal(["C-2", "C-1"], result.Boxes.Select(x => x.SourceBoxIdentity));
}
```

- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~SellFoxCartonParserTests|FullyQualifiedName~PackingTaskSourceReaderTests"`

Expected: FAIL because the parser, source contract, and reader do not exist.

- [ ] **Step 3: Implement capability probe and strict reader**

Use `INFORMATION_SCHEMA.COLUMNS` to verify `cartons_json` before selecting it. Canonicalize the header, ordered items, and ordered box identities with `System.Text.Json`, then compute a lower-case SHA-256 `SourceVersion`. Source weight/dimensions may appear only in the read-only box snapshot and must never populate WMS measurements.

```csharp
public interface IPackingTaskSourceReader : IDependency
{
    Task<PackingTaskSourceCapability> VerifyCapabilityAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackingTaskSourceSnapshot>> ReadAsync(
        IReadOnlyCollection<long> sourceTaskIds,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Run green tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingTask`

Expected: PASS for capability failure, duplicate/missing IDs, stable ordering, and task/item boundaries.

- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.Core/DBContext/Entities/ErpPackingTaskEntities.cs backend/ModernWMS.WMS/Entities/ViewModels/PackingTask backend/ModernWMS.WMS/IServices/PackingTask backend/ModernWMS.WMS/Services/PackingTask backend/ModernWMS.Tests/PackingTask
git commit -m "验证赛狐装箱任务物理箱身份"
```

### Task 2: Add the true order, task ownership, role warehouse, and generic box schema

**Files:**
- Create: `backend/ModernWMS.WMS/Entities/Models/DispatchWorkflow/DispatchOrderStatus.cs`
- Create: `backend/ModernWMS.WMS/Entities/Models/DispatchWorkflow/DispatchOrderEntity.cs`
- Create: `backend/ModernWMS.WMS/Entities/Models/DispatchWorkflow/DispatchPackingTaskEntity.cs`
- Create: `backend/ModernWMS.WMS/Entities/Models/DispatchWorkflow/DispatchPackingTaskItemEntity.cs`
- Create: `backend/ModernWMS.WMS/Entities/Models/DispatchWorkflow/DispatchSourceChangeEventEntity.cs`
- Create: `backend/ModernWMS.WMS/Entities/Models/Rolemenu/RoleWarehouseEntity.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchpicklistEntity.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWeighingBoxEntity.cs`
- Create: `backend/ModernWMS/Migrations/20260816090000_CreatePackingTaskDispatchWorkflow.cs`
- Modify: `backend/ModernWMS/Migrations/SqlDBContextModelSnapshot.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowModelTests.cs`

**Interfaces:**
- Consumes: Task 1 task/item/box identities and `SourceVersion`.
- Produces: `DispatchOrderStatus { PendingPick=20, Picked=30, Weighing=40, PendingOutbound=50, Outbound=60, SourceCancelled=90, ManualCancelled=91 }`.
- Produces: `wms_dispatch_order`, `wms_dispatch_packing_task`, `wms_dispatch_packing_task_item`, `wms_dispatch_source_change_event`, `wms_role_warehouse`.
- Produces unique keys on `dispatch_no`; nullable `active_source_task_id` (set to the source ID while active and cleared only on source/manual cancellation, allowing MySQL uniqueness without a filtered index); `(dispatch_order_id, source_task_id)`; `(packing_task_id, source_item_id)`; `(packing_task_id, source_box_identity)`; `(dispatch_order_id, source_version, decision)`; `(role_id, warehouse_id)`.
- Produces: `DispatchpicklistEntity.packing_task_item_id`. Generic weighing adds `packing_task_id`, `source_box_identity`, `source_snapshot`, `measurement_status`, and `copied_from_box_id`; legacy FBA fields become nullable and remain intact.

- [ ] **Step 1: Write failing model metadata tests**

```csharp
Assert.Equal("wms_dispatch_order", db.Model.FindEntityType(typeof(DispatchOrderEntity))!.GetTableName());
Assert.Contains(db.Model.FindEntityType(typeof(DispatchWeighingBoxEntity))!.GetIndexes(),
    x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["packing_task_id", "source_box_identity"]));
```

- [ ] **Step 2: Run red test**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowModelTests`

Expected: FAIL because entities/indexes do not exist.

- [ ] **Step 3: Implement entities and an additive migration**

`DispatchOrderEntity` includes dispatch/warehouse/status/idempotency, source versions/snapshots/freeze, accepted/adjudicated version, operator/time/reason, timestamps, and `[ConcurrencyCheck] row_version`. `DispatchPackingTaskItemEntity` preserves one source item per task. The migration must not update historical FBA rows.

- [ ] **Step 4: Run model test and migration script generation**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowModelTests`

Run: `dotnet ef migrations script 20260811170000_AddDispatchPickStockIdentity 20260816090000_CreatePackingTaskDispatchWorkflow --project backend/ModernWMS/ModernWMS.csproj --startup-project backend/ModernWMS/ModernWMS.csproj --no-build`

Expected: PASS; SQL is additive and contains no conversion `UPDATE`.

- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Entities/Models backend/ModernWMS/Migrations backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowModelTests.cs
git commit -m "建立装箱任务发货主单数据模型"
```

### Task 3: Enforce warehouse authorization and maintain role bindings

**Files:**
- Create: `backend/ModernWMS.WMS/Entities/ViewModels/Warehouse/WarehouseAccessViewModels.cs`
- Create: `backend/ModernWMS.WMS/IServices/Warehouse/IWarehouseAccessService.cs`
- Create: `backend/ModernWMS.WMS/Services/Warehouse/WarehouseAccessService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/Warehouse/WarehouseController.cs`
- Modify: `backend/ModernWMS.WMS/IServices/Rolemenu/IRolemenuService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Rolemenu/RolemenuService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/Rolemenu/RolemenuController.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/Rolemenu/RolemenuBothViewModel.cs`
- Modify: `frontend/src/types/Base/RoleMenu.ts`
- Modify: `frontend/src/api/base/roleMenu.ts`
- Modify: `frontend/src/view/base/roleMenu/add-or-update-role-menu.vue`
- Create: `frontend/src/view/base/roleMenu/roleWarehousePolicy.ts`
- Test: `frontend/src/view/base/roleMenu/roleWarehousePolicy.spec.ts`
- Test: `backend/ModernWMS.Tests/Warehouse/WarehouseAccessServiceTests.cs`
- Test: `backend/ModernWMS.Tests/Rolemenu/RoleWarehouseBindingTests.cs`

**Interfaces:**
- Consumes: Task 2 `RoleWarehouseEntity` and valid `erp_warehouse` rows.
- Produces: `IWarehouseAccessService.GetAllowedAsync(CurrentUser)` and `EnsureAllowedAsync(long warehouseId, CurrentUser)`.
- Produces: `GET /warehouse/access-options -> { warehouses, default_warehouse_id }`; admin receives all/default `320118`, ordinary roles receive bindings and first allowed ID or null.
- Produces: `GET /rolemenu/warehouses?userrole_id={id}` and `PUT /rolemenu/warehouses` consuming `{ userrole_id, warehouse_ids }`; replacement validates every ERP warehouse and commits atomically.

- [ ] **Step 1: Write failing tests** for admin all/default, unbound ordinary role, bound union, invalid binding, direct-request denial, and sorted/deduplicated UI payload.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~WarehouseAccess|FullyQualifiedName~RoleWarehouse"`

Run: `npm run test:unit -- src/view/base/roleMenu/roleWarehousePolicy.spec.ts`

Expected: FAIL before access/bindings exist.

- [ ] **Step 3: Implement backend authorization and role editor**

Resolve ordinary-role ID by exact normalized `CurrentUser.user_role`; do not use tenant ID as a warehouse visibility filter. All Task 4-8 services call `EnsureAllowedAsync` for page/detail/action/print.

```ts
expect(buildRoleWarehousePayload(7, [320118, 9, 320118])).toEqual({
  userrole_id: 7,
  warehouse_ids: [9, 320118]
})
```

- [ ] **Step 4: Run green tests and type-check**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~WarehouseAccess|FullyQualifiedName~RoleWarehouse"`

Run: `npm run test:unit -- src/view/base/roleMenu/roleWarehousePolicy.spec.ts`

Run: `npx vue-tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Entities/ViewModels/Warehouse backend/ModernWMS.WMS/IServices/Warehouse backend/ModernWMS.WMS/Services/Warehouse backend/ModernWMS.WMS/Controllers/Warehouse backend/ModernWMS.WMS/IServices/Rolemenu backend/ModernWMS.WMS/Services/Rolemenu backend/ModernWMS.WMS/Controllers/Rolemenu backend/ModernWMS.WMS/Entities/ViewModels/Rolemenu backend/ModernWMS.Tests/Warehouse backend/ModernWMS.Tests/Rolemenu frontend/src/types/Base/RoleMenu.ts frontend/src/api/base/roleMenu.ts frontend/src/view/base/roleMenu
git commit -m "按角色绑定发货仓库权限"
```

### Task 4: Create one same-warehouse order, reconcile it, and print it

**Files:**
- Create: `backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow/DispatchWorkflowViewModels.cs`
- Create: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Create: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchOrderQueryService.cs`
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.cs`
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Creation.cs`
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Reconciliation.cs`
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchOrderQueryService.cs`
- Create: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Modify: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/PackingTask/PackingTaskQueryViewModel.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowCreationTests.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowReconciliationTests.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowPrintTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3 source, model, and permission contracts.
- Produces: `POST /dispatch-workflow` with `{ warehouse_id, source_task_ids, idempotency_key }`; the key is the hash of sorted distinct task IDs.
- Produces: `POST /dispatch-workflow/page` with `{ status, warehouse_id, keyword, pageIndex, pageSize }`, `GET /dispatch-workflow/counts?warehouse_id=...`, plus `GET /dispatch-workflow/{id}`, `POST /dispatch-workflow/{id}/reconcile`, `GET /dispatch-workflow/{id}/print`.
- Produces: one summary row per order with `packing_task_nos`; print shape is order → ordered tasks → ordered task-owned items.
- Produces: API status codes are the exact strings `PENDING_PICK`, `PICKED`, `WEIGHING`, `PENDING_OUTBOUND`, `OUTBOUND`, `SOURCE_CANCELLED`, `MANUAL_CANCELLED`; `DispatchOrderStatus` numeric persistence never leaks into JSON.
- Produces: packing-task page warehouse filter and exclusion of tasks already linked to an active order.

- [ ] **Step 1: Write failing tests** for cross-warehouse rejection, task-set idempotency, active-task uniqueness, equal-SKU separation, partial/all cancellation, item rebuild, reconciled print, and print status immutability.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~DispatchWorkflowCreation|FullyQualifiedName~DispatchWorkflowReconciliation|FullyQualifiedName~DispatchWorkflowPrint"`

Expected: FAIL before workflow creation/reconciliation exists.

- [ ] **Step 3: Implement transactional creation/reconciliation**

Creation writes task/item snapshots only, never `DispatchpicklistEntity`. In `PendingPick`, replace only changed task items, remove cancelled tasks, release any not-yet-outbound allocation rows defensively, clear their `active_source_task_id`, set all-cancelled orders to `SourceCancelled`, and atomically save `SourceVersion`. Any reintroduced cross-warehouse source state rejects the whole reconcile.

- [ ] **Step 4: Implement query/print and run green tests**

Search by WMS dispatch number or any task number; every operation checks warehouse access. Print reconciles first and returns one complete snapshot only after success.

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflow`

- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow backend/ModernWMS.WMS/IServices/DispatchWorkflow backend/ModernWMS.WMS/Services/DispatchWorkflow backend/ModernWMS.WMS/Controllers/DispatchWorkflow backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs backend/ModernWMS.WMS/Entities/ViewModels/PackingTask/PackingTaskQueryViewModel.cs backend/ModernWMS.Tests/DispatchWorkflow
git commit -m "支持多装箱任务生成同仓拣货单"
```

### Task 5: Complete picking with whole-order stock validation

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Picking.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowPickingTests.cs`

**Interfaces:**
- Consumes: Task 4 reconcile result and task-owned items; `StockEntity`; `DispatchpicklistEntity.packing_task_item_id`.
- Produces: `POST /dispatch-workflow/{id}/complete-picking` with `{ request_id, row_version }`.
- Produces errors: `SOURCE_CHANGED`, `STOCK_SHORTAGE`, `CONCURRENCY_CONFLICT`, `STATUS_NOT_ALLOWED`; success moves the whole order to `Picked`.

- [ ] **Step 1: Write failing tests** proving creation has zero allocations, completion uses latest quantities, any shortage rolls back every allocation, a repeated request is idempotent, and success allocates each task item without merging equal SKUs.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowPickingTests`

Expected: FAIL before picking completion exists.

- [ ] **Step 3: Implement one serializable transaction**: permission/status/version check → reconcile → source-version recheck → stock read/lock → build every task-item allocation → reject any shortage → save all allocations → set `Picked`. Save nothing before every line passes.
- [ ] **Step 4: Run green tests** with the Step 2 command; expected PASS and zero allocations after the shortage case.
- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Picking.cs backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowPickingTests.cs
git commit -m "原子完成装箱任务拣货分配"
```

### Task 6: Freeze post-pick source changes and require a human decision

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.SourceAdjudication.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowSourceAdjudicationTests.cs`

**Interfaces:**
- Consumes: Task 1 source hash and Task 2 freeze/adjudication fields.
- Produces: `EnsurePostPickSourceCurrentAsync(int dispatchOrderId, CurrentUser, CancellationToken)`, called first when entering weighing, saving a box, completing task weighing, completing order weighing, and confirming outbound.
- Produces: `POST /dispatch-workflow/{id}/source-decision` with `{ decision: "CONTINUE"|"CANCEL", source_version, reason, request_id, row_version }`.
- Continue accepts the current WMS snapshot and unfreezes without changing item/allocation/measurement facts. Cancel releases unshipped allocations, logically invalidates measurements with audit retained, and sets `ManualCancelled`.

- [ ] **Step 1: Write failing tests** for all five guarded entries, accepted-version dedupe, later-version refreeze, idempotent decisions, required reason, cancel release, and outbound anomaly-only behavior.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowSourceAdjudicationTests`

Expected: FAIL before freeze/adjudication exists.

- [ ] **Step 3: Implement the transaction-start guard**. A new version persists only the diff event and `source_change_pending=true`, then returns `SOURCE_CHANGE_PENDING`; an already accepted hash passes; a later different hash freezes again.
- [ ] **Step 4: Run green tests** with the Step 2 command; expected PASS.
- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.SourceAdjudication.cs backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowSourceAdjudicationTests.cs
git commit -m "冻结拣货后来源变化并人工裁决"
```

### Task 7: Measure each existing SellFox physical box and copy safely

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Weighing.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowWeighingTests.cs`

**Interfaces:**
- Consumes: Task 1 verified boxes and Task 6 source guard.
- Produces: `POST /dispatch-workflow/{id}/start-weighing`; `GET /dispatch-workflow/{id}/tasks/{packingTaskId}/boxes`.
- Produces: `PUT /dispatch-workflow/{id}/boxes/{boxId}` with positive `weight/length/width/height`, `request_id`, `row_version`.
- Produces: `POST /dispatch-workflow/{id}/boxes/{targetBoxId}/copy` with `source_box_id`, `request_id`, `row_version`; both boxes must already exist under one task.
- Produces: `POST /dispatch-workflow/{id}/tasks/{packingTaskId}/complete-weighing` and `POST /dispatch-workflow/{id}/complete-weighing`.

- [ ] **Step 1: Write failing tests** for capability fail-closed, exact source-box materialization, rejecting new/foreign boxes, positive values, same-task copy, editing after copy, per-task completion, and all-task completion.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowWeighingTests`

Expected: FAIL before task-scoped box weighing exists.

- [ ] **Step 3: Implement guarded measurement operations**. Materialize boxes idempotently from `cartons_json`; never put source dimensions into measured fields; every mutation starts with Task 6 guard and optimistic concurrency.
- [ ] **Step 4: Run green tests** with the Step 2 command; expected PASS.
- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Weighing.cs backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowWeighingTests.cs
git commit -m "按赛狐物理箱记录仓库实测数据"
```

### Task 8: Confirm outbound atomically and preserve audited reversal/sign

**Files:**
- Create: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs`
- Modify: `backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowOutboundTests.cs`

**Interfaces:**
- Consumes: fully measured order, exact stock allocations, Task 6 guard, carrier/unit settings.
- Produces: `POST /dispatch-workflow/{id}/confirm-outbound` with `{ request_id, row_version }`; one transaction deducts stock, closes allocations, writes action log, and sets `Outbound`.
- Produces: `POST /dispatch-workflow/{id}/cancel-outbound` restores exact allocated stock rows and returns to `PendingOutbound`; `POST /dispatch-workflow/{id}/sign` records sign facts.

- [ ] **Step 1: Write failing tests** for incomplete measurement, freeze rejection, exact-row deduction, rollback on conflict, idempotent outbound, exact restoration, and outbound source anomaly audit.
- [ ] **Step 2: Run red tests**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflowOutboundTests`

Expected: FAIL before order-level outbound exists.

- [ ] **Step 3: Implement outbound/reversal transactions** without routing new orders through legacy FBA-number logic.
- [ ] **Step 4: Run green tests** with the Step 2 command; expected PASS.
- [ ] **Step 5: Commit**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs backend/ModernWMS.WMS/IServices/DispatchWorkflow/IDispatchWorkflowService.cs backend/ModernWMS.WMS/Controllers/DispatchWorkflow/DispatchWorkflowController.cs backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowOutboundTests.cs
git commit -m "原子完成装箱任务发货出库"
```

### Task 9: Freeze shared frontend contracts, i18n, search, and counts

**Files:**
- Create: `frontend/src/types/DeliveryManagement/DispatchWorkflow.ts`
- Create: `frontend/src/api/wms/dispatchWorkflow.ts`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.spec.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/deliveryManagement.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.spec.ts`
- Modify: `frontend/src/languages/langsJson/cn.json`

**Interfaces:**
- Consumes: Tasks 3-8 HTTP routes and errors.
- Produces: TS `DispatchOrderStatus`, `DispatchOrderSummaryVO`, `DispatchOrderDetailVO`, `DispatchPackingTaskVO`, `DispatchPackingTaskItemVO`, `DispatchWeighingBoxVO`, `WarehouseAccessVO`, request shapes, and error union.
- Produces API functions: `getWarehouseAccess`, `createDispatchOrder`, `getDispatchOrderPage`, `getDispatchStatusCounts`, `getDispatchOrderDetail`, `getDispatchPrintData`, `completeDispatchPicking`, `startDispatchWeighing`, `saveDispatchBox`, `copyDispatchBox`, `completeTaskWeighing`, `completeDispatchWeighing`, `decideSourceChange`, `confirmDispatchOutbound`, `cancelDispatchOutbound`.
- Produces counts for `PENDING_PICK`, `PICKED`, `WEIGHING`, `PENDING_OUTBOUND`, `OUTBOUND`; searches match dispatch number or any task number.

- [ ] **Step 1: Write failing tests** for status-to-tab mapping, source-error actions, warehouse default, and one-order-one-row identity.
- [ ] **Step 2: Run red tests**

Run: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.spec.ts src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.spec.ts`

Expected: FAIL before shared contracts exist.

- [ ] **Step 3: Implement shared types/API/host wiring**. Retain legacy APIs only for historical reads; none of the six new-flow pages call FBA creation APIs.
- [ ] **Step 4: Run green tests and type-check**

Run: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.spec.ts src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.spec.ts`

Run: `npx vue-tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types/DeliveryManagement/DispatchWorkflow.ts frontend/src/api/wms/dispatchWorkflow.ts frontend/src/view/deliveryManagement/deliveryManagement/deliveryManagement.vue frontend/src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.ts frontend/src/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy.spec.ts frontend/src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.ts frontend/src/view/deliveryManagement/deliveryManagement/deliveryStatusCounts.spec.ts frontend/src/languages/langsJson/cn.json
git commit -m "统一装箱任务发货前端契约"
```

### Task 10: Packing-task source page

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-list.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/packingTaskSelection.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/packingTaskSelection.spec.ts`

**Interfaces:** Consumes Task 9 warehouse/task/create APIs; produces one selectable row per stable task, admin default `320118`, same-warehouse request, deterministic idempotency key, and post-success removal.

- [ ] **Step 1: Write tests** for default warehouse, same/cross-warehouse selection, dedupe, and deterministic request key.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/packingTaskSelection.spec.ts`; expect FAIL.
- [ ] **Step 3: Implement warehouse selector, search, paging, task expansion, multi-select, and “生成待拣货单”.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务来源选择页"`.

### Task 11: Pending-pick page and expanded print

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabGoodsToBePicked.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/pendingPickPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/pendingPickPolicy.spec.ts`

**Interfaces:** Consumes pending/detail/print/complete APIs; produces one order row, task labels, task-owned details, full order→task→item print, and no print transition.

- [ ] **Step 1: Write tests** for row grouping, equal-SKU task separation, expanded print, shortage/source-change messages.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/pendingPickPolicy.spec.ts`; expect FAIL.
- [ ] **Step 3: Implement list/detail/print/人工拣货完成; shortage refreshes in place and never enters picked.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务待拣货页面"`.

### Task 12: Picked page and human source decision

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabPicked.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/pickedOrderPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/pickedOrderPolicy.spec.ts`

**Interfaces:** Consumes picked/detail/start/decision APIs; produces one row, freeze/diff badge, required-reason continue/cancel dialog, and disabled weighing while frozen.

- [ ] **Step 1: Write tests** for row identity, freeze action policy, reason requirement, and decision payloads.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/pickedOrderPolicy.spec.ts`; expect FAIL.
- [ ] **Step 3: Implement without legacy `repick` or FBA shipment identity for new orders.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务已拣货页面"`.

### Task 13: Task-scoped physical-box weighing and copy

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabWeighed.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/shipment-box-weigh-dialog.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/dispatchBoxMeasurement.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/dispatchBoxMeasurement.spec.ts`

**Interfaces:** Consumes task/box APIs; produces order→task→existing source boxes, positive kg/cm values, same-task copy-to-existing, editable copies, task completion, and whole-order completion.

- [ ] **Step 1: Write tests** for completeness, copy-without-create, same-task restriction, editable copied values, and capability failure.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/dispatchBoxMeasurement.spec.ts`; expect FAIL.
- [ ] **Step 3: Replace FBA/ERP box identifiers with `packing_task_id`/`source_box_identity`; never prefill measured values from source.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务逐箱称重页面"`.

### Task 14: Pending-outbound page

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabDelivered.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/pendingOutboundPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/pendingOutboundPolicy.spec.ts`

**Interfaces:** Consumes pending-outbound/detail/carrier/decision/confirm APIs; produces one order row, all task numbers, measurement summary, freeze handling, and backend-validated confirmation.

- [ ] **Step 1: Write tests** for row identity, readiness, freeze, carrier validation, and confirm payload.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/pendingOutboundPolicy.spec.ts`; expect FAIL.
- [ ] **Step 3: Implement using WMS order ID and remove one-row-equals-one-SKU/FBA assumptions.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务待出库页面"`.

### Task 15: Completed-outbound page

**Files:**
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tabCompleted.vue`
- Create: `frontend/src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.ts`
- Test: `frontend/src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`

**Interfaces:** Consumes outbound/detail/reversal/sign APIs; produces one read-only order row, task-number search/display, full task/item/box detail, and authorized audited reversal/sign actions.

- [ ] **Step 1: Write tests** for one-row projection, task-number search, read-only measurements, and reversal payload.
- [ ] **Step 2: Run red**: `npm run test:unit -- src/view/deliveryManagement/deliveryManagement/completedOutboundPolicy.spec.ts`; expect FAIL.
- [ ] **Step 3: Implement; later source changes render as audit warnings and never mutate completed facts.**
- [ ] **Step 4: Run green test and `npx vue-tsc --noEmit`; expect PASS.**
- [ ] **Step 5: Commit only these files** with `git commit -m "改造装箱任务已出库页面"`.

### Task 16: Integration, migration, and full-flow verification

**Files:**
- Create: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowContractTests.cs`
- Create: `frontend/tests/smoke/packing-task-dispatch-workflow.spec.ts`
- Modify: `docs/PACKING_TASK_QUERY.md`
- Modify only for verified integration defects: files already owned by Tasks 1-15

**Interfaces:** Consumes Tasks 1-15; produces proof that migration, controllers, TS contracts, permissions, counts, and the six-page journey agree.

- [ ] **Step 1: Add contract tests** for routes, request fields, status values, and `SOURCE_CHANGE_PENDING`, `STOCK_SHORTAGE`, `CONCURRENCY_CONFLICT`.
- [ ] **Step 2: Run backend verification**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj`

Run: `dotnet build backend/ModernWMS.sln --no-restore`

Expected: all PASS.

- [ ] **Step 3: Apply migration only to an explicitly confirmed disposable/test MySQL database**

Precondition: set `MODERNWMS_TEST_CONNECTION` to a reviewed disposable MySQL connection string and abort when it is empty.

Run: `dotnet ef database update --project backend/ModernWMS/ModernWMS.csproj --startup-project backend/ModernWMS/ModernWMS.csproj --connection "$env:MODERNWMS_TEST_CONNECTION"`

Expected: new `wms_` objects exist and historical rows are unchanged. Never run against an unconfirmed or production database.

- [ ] **Step 4: Add/run the browser journey**: admin default `320118` → select two same-warehouse tasks → one pending row/two labels → expanded print/no status change → shortage remains pending → successful pick → source freeze/continue → weigh/copy/edit existing boxes → complete all tasks → outbound → one completed row.

Run: `npm run test:unit`

Run: `npm run build`

Run: `npm run test:e2e -- tests/smoke/packing-task-dispatch-workflow.spec.ts`

Expected: all PASS.

- [ ] **Step 5: Verify the SellFox boundary**

Run: `rg -n "SellFox|sellfox" backend/ModernWMS.WMS frontend/src`

Expected: no HTTP client, URL, per-box callback, or write-back endpoint; only shared-table reads, source identity/snapshots, and documentation state that write-back is excluded.

- [ ] **Step 6: Document required source columns, stable-ID failure behavior, role binding, release order, statuses, and excluded write-back; commit**

```bash
git add backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowContractTests.cs frontend/tests/smoke/packing-task-dispatch-workflow.spec.ts docs/PACKING_TASK_QUERY.md
git commit -m "验证装箱任务驱动发货全流程"
```

## Execution and Review Gates

1. Tasks 1-3 are strict prerequisites.
2. Tasks 4-8 are sequential shared-backend work owned by one backend worker; run and review focused tests after each commit.
3. Task 9 freezes shared frontend contracts.
4. Tasks 10-15 may run in parallel with one worker per exclusive file set; they cannot touch shared backend/API/types/i18n files.
5. Task 16 is the integration gate. Production DDL, SellFox HTTP/write-back, and historical conversion remain prohibited.
