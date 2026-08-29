# ModernWMS `trk_stock` 装箱库存主合同 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 ModernWMS 的装箱候选、绑定、预占、调整、换绑、取消、释放、实际装箱和出库只以 `trk_stock.id` 为库存身份，并删除租户、库存模式、维护门槛、仓库业务编号映射和位置分配前置依赖。

**Architecture:** 新装箱链通过 `PackingStockMutationService` 直接锁定并更新 `trk_stock`、共享预占主明细和 `trk_stock_record`；历史 allocation 行由独立兼容适配器只读并结清。Sellfox 任务创建人唯一解析为 `system_users.id`，候选只在同创建人、同仓范围内查询，匹配商品仅影响排序。

**Tech Stack:** ASP.NET Core 10、C# 14、Dapper、MySqlConnector、MySQL 8、xUnit、Vue 3、TypeScript 6、Vuetify、Vitest、Flyway 11.15.0。

**Spec:** `docs/superpowers/specs/2026-08-29-modernwms-trk-stock-packing-contract-design.md`

## Global Constraints

- 只允许修改、测试和提交 `/root/erp/ModernWMS`。
- 开发测试数据库只允许目标主机 `192.168.100.2`；生产库禁止连接、读取和写入。
- 不调用或恢复 Ruoyi HTTP 接口。
- 新绑定只允许任务创建人自己的同仓库存；商品不匹配仍可选择。
- 绑定生命周期不得要求库区、库位或位置分配。
- 初始预占为任务量乘变体数量；库存不足、超计划和负库存不新增阻断。
- `SHIP_OUT` 成功后不可回退。
- 已执行的 Flyway 版本文件不可修改；修正必须新增版本。
- 保护未跟踪文件 `codex_resume.txt` 和所有非本任务改动。
- 每个生产行为先写失败测试并观察预期失败，再写最小实现。

---

### Task 1: 建立失败基线与架构护栏

**Files:**
- Create: `backend/ModernWMS.Tests/StockAllocation/PackingStockMutationContractTests.cs`
- Create: `backend/ModernWMS.Tests/Architecture/NoInventoryRuntimeModeDependencyTests.cs`
- Modify: `backend/ModernWMS.Tests/Architecture/NoTenantDependencyTests.cs`
- Modify: `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`

**Interfaces:**
- Produces: 对无条件跨租户异常、同人同仓边界、无位置绑定和运行模式依赖的失败证据。
- Consumes: 当前 `StockAllocationMutationService`、`PackingTaskQueryService` 生产程序集。

- [ ] **Step 1: 写无条件异常回归测试**

新增测试 `Prelock_contract_does_not_reject_every_reservation_as_cross_scope`，通过一个最小可测试的预锁输入策略调用证明合法单仓请求不会在访问数据库前无条件抛出。测试必须因当前“批量预锁包含跨租户预占来源”分支失败。

```csharp
[Fact]
public void Prelock_contract_does_not_reject_every_reservation_as_cross_scope()
{
    var result = PackingStockPrelockPolicy.Validate([320118],
        [new PackingStockPrelockIdentity(320118, 9001)]);
    Assert.Equal([9001L], result.StockIds);
}
```

- [ ] **Step 2: 写候选边界失败测试**

为数据源合同增加真实输入集合，断言同人同仓两条商品都返回、匹配商品在前、他人和他仓不返回；生产变化若按 SKU 硬过滤或按操作者过滤必须使测试失败。

```csharp
Assert.Equal([matchedStockId, otherProductStockId], result.Rows.Select(x => x.erp_stock_id));
Assert.DoesNotContain(result.Rows, x => x.order_user_id != taskOwnerId || x.warehouse_id != taskWarehouseId);
```

- [ ] **Step 3: 写程序集运行模式护栏测试**

扫描生产程序集的类型、成员和 IL 字符串，失败条件为出现 `LEGACY_READ`、`CANONICAL_ERP`、`maintenance_enabled`、`wms_inventory_runtime_config`、`库存维护窗口` 或 `旧库存模式`。

- [ ] **Step 4: 强化去租户护栏**

把中文“跨租户”加入生产程序集违规判定，保留迁移合同测试对物理删列的验证。

- [ ] **Step 5: 运行 RED**

Run:

```bash
dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~PackingStockMutationContractTests|FullyQualifiedName~NoInventoryRuntimeModeDependencyTests|FullyQualifiedName~NoTenantDependencyTests|FullyQualifiedName~PackingTaskQueryServiceTests"
```

Expected: 无条件异常、运行模式和旧候选合同相关测试失败，失败原因与目标缺失一致。

- [ ] **Step 6: 提交测试基线**

```bash
git add backend/ModernWMS.Tests/StockAllocation/PackingStockMutationContractTests.cs backend/ModernWMS.Tests/Architecture/NoInventoryRuntimeModeDependencyTests.cs backend/ModernWMS.Tests/Architecture/NoTenantDependencyTests.cs backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs
git commit -m "测试：锁定装箱库存主合同"
```

### Task 2: 引入直接 `trk_stock` 的预占与库存变更核心

**Files:**
- Create: `backend/ModernWMS.WMS/Services/StockAllocation/PackingStockPrelockPolicy.cs`
- Create: `backend/ModernWMS.WMS/Services/StockAllocation/PackingStockMutationService.cs`
- Create: `backend/ModernWMS.WMS/IServices/StockAllocation/IPackingStockMutationService.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockReservationMutationCoordinator.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs`
- Modify: `backend/ModernWMS.WMS/IServices/StockAllocation/StockAllocationMutationModels.cs`
- Test: `backend/ModernWMS.Tests/StockAllocation/PackingStockMutationContractTests.cs`
- Test: `backend/ModernWMS.Tests/StockAllocation/StockBalanceInvariantTests.cs`

**Interfaces:**
- Produces: `IPackingStockMutationService.PrelockAsync`, `ReserveAsync`, `ReleaseAsync`, `ShipLockedAsync`, `AdjustAvailableAsync`，全部只接收 `erpStockId`。
- Consumes: `StockMutationContext`、`trk_stock_reservation*`、`trk_stock`、`trk_stock_record`。

- [ ] **Step 1: 写直接库存 mutation 失败测试**

测试愿望接口：

```csharp
Task<PackingStockMutationResult> ReserveAsync(
    IDbConnection connection, IDbTransaction transaction,
    StockMutationContext context, long erpStockId, long quantity,
    CancellationToken cancellationToken = default);
```

断言请求指纹和结果不包含 allocation，预占数量守恒为 available 减、occupied 加、total 不变。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingStockMutationContractTests`

Expected: 接口或实现不存在而失败。

- [ ] **Step 3: 实现预锁策略并删除无条件异常**

`PackingStockPrelockPolicy.Validate` 只验证仓库 ID 和库存身份有效，不再包含租户语义；`StockAllocationMutationService` 的历史 API 删除跨租户错误文案和运行配置锁。

- [ ] **Step 4: 实现 stock-only mutation**

固定锁顺序为共享预占所有者、`trk_stock`、幂等操作。更新语义：

```text
LOCK: available -= q; occupied += q; total unchanged
UNLOCK: available += q; occupied -= q; total unchanged
SHIP_OUT: occupied -= q; total -= q
ADJUST: available += q; total += q
```

`occupied_qty` 不能低于零；available/total 允许业务欠账。每次成功写 `trk_stock_record` 和共享命令结果。

- [ ] **Step 5: 让共享预占不再创建位置分解**

`StockReservationMutationCoordinator.BeginMutationAsync` 移除 `allocationId` 参数及 `wms_erp_stock_reservation_allocation` 新写；命令指纹只包含 stock、数量、所有者版本和动作。

- [ ] **Step 6: 运行 GREEN 和数量守恒测试**

```bash
dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~PackingStockMutationContractTests|FullyQualifiedName~StockBalanceInvariantTests"
```

- [ ] **Step 7: 提交 mutation 核心**

```bash
git add backend/ModernWMS.WMS/Services/StockAllocation backend/ModernWMS.WMS/IServices/StockAllocation backend/ModernWMS.Tests/StockAllocation
git commit -m "重构：装箱预占直接引用ERP库存"
```

### Task 3: 重写候选查询和 API 合同

**Files:**
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/PackingTask/PackingTaskStockSelectionViewModels.cs`
- Modify: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- Modify: `backend/ModernWMS.WMS/IServices/PackingTask/IPackingTaskQueryService.cs`
- Modify: `backend/ModernWMS.WMS/Controllers/PackingTask/PackingTaskQueryController.cs`
- Modify: `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`
- Create: `backend/ModernWMS.Tests/PackingTask/PackingTaskStockMySqlIntegrationTests.cs`

**Interfaces:**
- Produces: `PackingTaskStockPageRequest` 只含 task/item/page/keyword；`SelectableStockViewModel` 以 `erp_stock_id` 为行身份；`PackingTaskStockSelectRequest` 只含 task/item/erpStock/variant/selection。
- Consumes: `ruiyi_sellfox_packing_task*`、`system_users`、`trk_stock`。

- [ ] **Step 1: 写创建人解析与候选排序测试**

覆盖唯一用户、零用户、重名；断言零用户和重名返回明确错误，绝不放宽候选范围。

- [ ] **Step 2: 写无映射候选测试**

在测试库种一条没有 `wms_erp_stock_allocation`、WMS SKU、货主和位置映射的 `trk_stock`，断言仍返回并可标记匹配/非匹配。

- [ ] **Step 3: 运行 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingTask`

- [ ] **Step 4: 实现直接候选 SQL**

上下文查询不连接 `wms_warehouse`；仓库名称使用任务快照或 `erp_warehouse`。候选 SQL 从 `trk_stock` 起表，以 resolved owner 和 task warehouse 为硬条件，LEFT JOIN 商品图片只用于展示。

- [ ] **Step 5: 删除旧 DTO 字段和双模式分支**

移除 `search_others`、location、owner、legacy stock/allocation 字段和 `LoadRuntimeAsync`；分页 totals 与 rows 使用同一硬边界。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingTask`

- [ ] **Step 7: 提交候选合同**

```bash
git add backend/ModernWMS.WMS/Entities/ViewModels/PackingTask backend/ModernWMS.WMS/Services/PackingTask backend/ModernWMS.WMS/IServices/PackingTask backend/ModernWMS.WMS/Controllers/PackingTask backend/ModernWMS.Tests/PackingTask
git commit -m "重构：候选库存直接查询trk_stock"
```

### Task 4: 重写绑定、调整、换绑、取消和历史释放

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- Create: `backend/ModernWMS.WMS/Services/PackingTask/LegacyPackingSelectionReleaseAdapter.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/PackingTask/PackingTaskStockSelectionEntity.cs`
- Test: `backend/ModernWMS.Tests/PackingTask/PackingTaskStockMySqlIntegrationTests.cs`
- Test: `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`

**Interfaces:**
- Produces: 新选择只写 `erp_stock_id`、共享 reservation IDs、商品/所有人快照；兼容适配器只结清历史 allocation。
- Consumes: `IPackingStockMutationService`。

- [ ] **Step 1: 写生命周期失败测试**

分别覆盖首次绑定、同库存增减、换绑、取消、重复命令、没有位置分配、负 available、历史 allocation 释放。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingTaskStockMySqlIntegrationTests`

- [ ] **Step 3: 实现服务端硬校验和目标数量计算**

事务内重新锁定任务、唯一解析创建人、锁定 `trk_stock` 并比较 owner/warehouse；目标数量固定为 `task_num * variant`。

- [ ] **Step 4: 实现 stock-only 选择生命周期**

新行的 `stock_id=0`、`stock_allocation_id=NULL`、位置兼容字段为 NULL；新增/更新/换绑调用 stock-only mutation，并保留取消审计与 row_version。

- [ ] **Step 5: 实现历史释放适配器**

如果活动选择携带 allocation，只在主共享预占和 `trk_stock` 释放成功后结清历史位置分解；不得创建新位置行，无法映射的记录返回明确清理证据。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~PackingTaskStockMySqlIntegrationTests|FullyQualifiedName~PackingTaskQueryServiceTests"`

- [ ] **Step 7: 提交生命周期**

```bash
git add backend/ModernWMS.WMS/Services/PackingTask backend/ModernWMS.WMS/Entities/Models/PackingTask backend/ModernWMS.Tests/PackingTask
git commit -m "重构：装箱绑定脱离位置分配"
```

### Task 5: 让拣货、实际装箱和出库沿用 `erp_stock_id`

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.ActualPacking.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Picking.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Rollback.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Reconciliation.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.SourceAdjudication.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/ActualPackingLinePolicy.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/ActualPackingMaterializationPolicy.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowEntities.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow/DispatchWorkflowViewModels.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingLinePolicyTests.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/ActualPackingMaterializationPolicyTests.cs`
- Test: `backend/ModernWMS.Tests/DispatchWorkflow/DispatchWorkflowEndpointTests.cs`

**Interfaces:**
- Produces: 实际装箱行按 `erp_stock_id` 分组，位置/WMS SKU/货主为可空历史快照。
- Consumes: stock-only mutation 和 Task 4 选择记录。

- [ ] **Step 1: 写 `erp_stock_id` 分组失败测试**

相同 `erp_stock_id`、不同/空 allocation 的行必须归并为同一库存事实；无位置行可完成 materialization。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflow`

- [ ] **Step 3: 修改计划和实装 DTO/实体**

新增 commodity/order-user 快照，使 `stock_allocation_id`、`goods_location_id`、`goods_owner_id`、`wms_sku_id` 可空；业务 key 改为 task item + `erp_stock_id`。

- [ ] **Step 4: 修改预占调整、回退和出库调用**

全部使用 `IPackingStockMutationService`；SHIP_OUT 仍只消费剩余预占且不可逆，回退只处理未 SHIP_OUT 的状态。

- [ ] **Step 5: 运行 GREEN**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~DispatchWorkflow`

- [ ] **Step 6: 提交履约链**

```bash
git add backend/ModernWMS.WMS/Services/DispatchWorkflow backend/ModernWMS.WMS/Entities/Models/Dispatchlist backend/ModernWMS.WMS/Entities/ViewModels/DispatchWorkflow backend/ModernWMS.Tests/DispatchWorkflow
git commit -m "重构：履约链以ERP库存作为身份"
```

### Task 6: 删除库存模式和仓库维护门槛

**Files:**
- Delete: `backend/ModernWMS.Core/DBContext/Entities/WmsInventoryRuntimeConfigEntity.cs`
- Delete: `backend/ModernWMS.WMS/Services/Stock/InventoryRuntimePolicy.cs`
- Delete: `backend/ModernWMS.Tests/Asn/InventoryRuntimePolicyTests.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stock/StockService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stockadjust/StockadjustService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stockmove/StockmoveService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stockfreeze/StockfreezeService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stockprocess/StockprocessService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stocktaking/StocktakingService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Asn/AsnService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.CanonicalInventory.cs`
- Modify: `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistPickingService.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.cs`
- Modify: runtime fields in stock view models and frontend stock types.
- Test: `backend/ModernWMS.Tests/Architecture/NoInventoryRuntimeModeDependencyTests.cs`

**Interfaces:**
- Produces: 单一路径库存读写，无运行配置表和维护门槛。
- Consumes: 每个模块已有 canonical `trk_stock` 路径；旧 `wms_stock` 只保留显式历史查询。

- [ ] **Step 1: 逐模块写/扩展失败护栏**

架构测试列出所有剩余生产引用；每移除一组引用后重跑，避免遗漏 DTO 和中文文案。

- [ ] **Step 2: 删除运行配置实体和策略**

移除 DI/实体引用；ERP 签收入库直接进入 `trk_stock` 路径。

- [ ] **Step 3: 折叠双路由服务**

库存查询、调整、移库、冻结、加工、盘点、ASN、出库不再读取 mode/maintenance。需要历史兼容时按数据行是否有 `erp_stock_id` 明确选择兼容适配器，不使用仓库开关。

- [ ] **Step 4: 运行 GREEN**

```bash
dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~NoInventoryRuntimeModeDependencyTests|FullyQualifiedName~Asn|FullyQualifiedName~Stock|FullyQualifiedName~Dispatchlist"
```

- [ ] **Step 5: 提交运行模式删除**

```bash
git add backend frontend/src/types
git commit -m "重构：删除库存运行模式和维护门槛"
```

### Task 7: 同步 ModernWMS 选择库存前端

**Files:**
- Modify: `frontend/src/types/DeliveryManagement/PackingTask.ts`
- Modify: `frontend/src/api/wms/dispatchWorkflow.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/select-stock-dialog.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingTaskSelection.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingTaskSelection.spec.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.spec.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.spec.ts`

**Interfaces:**
- Produces: 以 `erp_stock_id` 为行身份的弹窗和实际装箱编辑器。
- Consumes: Task 3/5 API DTO。

- [ ] **Step 1: 写前端失败测试**

断言请求不包含 `search_others`/location/owner/allocation，其他商品可选择，零/负可用量不在前端阻断，行身份为 `erp_stock_id`。

- [ ] **Step 2: 运行 RED**

```bash
cd frontend
npm run test:unit -- src/view/deliveryManagement/deliveryManagement/packingTaskSelection.spec.ts src/view/deliveryManagement/deliveryManagement/packing-task-weighing-editor.spec.ts src/view/deliveryManagement/deliveryManagement/packingPlanPolicy.spec.ts
```

- [ ] **Step 3: 修改类型和弹窗**

删除跨创建人和位置 UI；保留商品搜索；按 selected/matched/ID 显示，使用 `erp_stock_id` 选择、更新和取消。

- [ ] **Step 4: 修改实际装箱编辑器**

库存选项 value 改为 `erp_stock_id`，位置和 allocation 不再是完成校验条件。

- [ ] **Step 5: 运行 GREEN 和构建**

```bash
cd frontend
npm run test:unit
npm run build
```

- [ ] **Step 6: 提交前端**

```bash
git add frontend/src
git commit -m "前端：选择库存脱离位置和跨创建人入口"
```

### Task 8: 新增迁移、回退和数据库表清理报告

**Files:**
- Create: `flyway/sql/V20260829150000__packing_stock_identity_and_remove_runtime_gate.sql`
- Create: `flyway/manual/V20260829150000__packing_stock_identity_rollback.sql`
- Create: `docs/database/2026-08-29-inventory-table-cleanup-report.md`
- Modify: `flyway/README.md`
- Modify: `flyway/manual/erp_stock_allocation_cutover.sql`
- Modify/Delete: tenant/runtime legacy manual cutover variants as justified by references.
- Create: `backend/ModernWMS.Tests/Database/PackingStockIdentityMigrationContractTests.cs`
- Modify: `backend/ModernWMS.Tests/Database/RemoveTenantMigrationContractTests.cs`

**Interfaces:**
- Produces: 可审查的前向 DDL、无租户回退 DDL、表清理分类和删除前置条件。
- Consumes: Task 2–7 最终字段合同。

- [ ] **Step 1: 写迁移合同失败测试**

测试执行迁移于开发测试 schema，断言运行配置表删除、position 兼容列可空、`erp_stock_id` 索引存在、新 migration 不写 `tenant_id`。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter FullyQualifiedName~PackingStockIdentityMigrationContractTests`

- [ ] **Step 3: 编写前向迁移和回退脚本**

迁移先检测活动键冲突再 DDL；回退不恢复 tenant，不伪造位置。所有脚本开头声明开发演练和生产审批边界。

- [ ] **Step 4: 编写表清理报告**

按“主链必需、其他仓内业务使用、仅历史兼容、可后续删除、本次已删除”分类，列出扫描命令、代码引用、数据检查、备份要求和删除顺序。

- [ ] **Step 5: 在 `192.168.100.2` 演练**

连接前从实际连接配置解析主机并断言精确等于 `192.168.100.2`。创建任务专用测试 schema，执行当前全量 migration、前向 migration、守恒查询、回退脚本和再次前向 migration。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~Database|FullyQualifiedName~NoTenantDependencyTests|FullyQualifiedName~NoInventoryRuntimeModeDependencyTests"`

- [ ] **Step 7: 提交数据库合同**

```bash
git add flyway docs/database backend/ModernWMS.Tests/Database
git commit -m "数据库：迁移装箱库存身份并删除运行门槛"
```

### Task 9: 完整验证、真实接口和日志证据

**Files:**
- Modify only if verification exposes a tested defect.
- Update: `docs/database/2026-08-29-inventory-table-cleanup-report.md` with final reference evidence.

**Interfaces:**
- Produces: 后端/前端/数据库/接口/日志验证证据和最终提交。
- Consumes: Tasks 1–8 完整实现。

- [ ] **Step 1: 运行完整后端测试**

```bash
dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --configuration Release
```

- [ ] **Step 2: 运行完整后端构建**

```bash
dotnet build backend/ModernWMS.sln --configuration Release --no-restore
```

- [ ] **Step 3: 运行前端测试和生产构建**

```bash
cd frontend
npm run test:unit
npm run build
```

- [ ] **Step 4: 启动临时服务并做真实接口测试**

确认数据库主机为 `192.168.100.2` 后，用非 watch 模式启动临时后端；登录后依次调用候选、绑定、调整、换绑、取消和释放。使用任务专用测试数据，记录响应状态与库存前后守恒查询。

- [ ] **Step 5: 检查日志并清理测试现场**

日志不得出现 `operation_failed`、跨租户、运行模式、维护窗口或位置缺失异常。停止临时服务，删除仅由本任务创建的开发测试数据或测试 schema，不触碰用户既有数据。

- [ ] **Step 6: 最终静态扫描**

```bash
rg -n -i --glob 'backend/**/*.cs' --glob 'frontend/src/**' 'tenantId|tenant_id|跨租户|LEGACY_READ|CANONICAL_ERP|maintenance_enabled|wms_inventory_runtime_config'
git diff --check
git status --short
```

允许的 `tenant_id` 仅限不可变历史 migration 和物理删列合同；生产代码、前端和新 migration 必须零匹配。

- [ ] **Step 7: 复核数据库表清理报告**

逐表核对代码引用和开发库数据量，明确哪些表当前可删、哪些必须等历史释放/其他仓内模块迁移后再删。

- [ ] **Step 8: 提交验证期修正与报告**

```bash
git add docs/database/2026-08-29-inventory-table-cleanup-report.md
git commit -m "验证：完成装箱库存主合同回归"
```

- [ ] **Step 9: 更新 Handoff**

追加实际提交号、测试数量、构建输出、开发测试库迁移/回退、真实接口、日志和未触碰生产库证据；只有全部验收完成才设为 `COMPLETED`。
