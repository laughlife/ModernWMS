# ModernWMS 全仓取消租户依赖 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从 ModernWMS 生产代码、前端、接口模型、SQL、索引、唯一键和数据库物理结构中彻底删除全部 `tenantId`/`tenant_id` 依赖，同时保留真实权限、仓库、货主、库存所属人、装箱预占和 CAS 边界。

**Architecture:** 先用编译后程序集契约测试建立无租户红线，再由身份层向库存与装箱主链路逐层删除租户参数和 SQL。数据库只新增一个 Flyway 迁移，使用无租户业务键替换旧约束并删除所有相关物理列；其它仓库不修改，数据库迁移不执行。

**Tech Stack:** .NET 10、xUnit、Dapper、MySqlConnector、Vue 3、TypeScript 6、Vitest 4、Flyway/MySQL。

**Spec:** `docs/superpowers/specs/2026-08-27-modernwms-remove-tenancy-design.md`

## Global Constraints

- 只允许写 `/root/erp/ModernWMS`。
- 不修改已发布的 Flyway 历史迁移；新建版本化迁移。
- 不实际执行数据库迁移，不启动或重启开发服务。
- 不保留 tenant 可空列、默认列、固定 0/1、全局常量、COALESCE 或跨仓兼容列。
- 不弱化用户、角色、菜单/API、角色仓库、仓库、货主、库区、库位、任务所属人、库存所属人、reservation/sourceLineKey 和 CAS 校验。
- 不暂存或提交用户已有开发配置、启动脚本和 `docs/bak/` 改动。

---

### Task 1: 建立编译后无租户契约测试

**Files:**
- Create: `backend/ModernWMS.Tests/Architecture/NoTenantDependencyTests.cs`
- Modify: `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`
- Create: `backend/ModernWMS.Tests/Security/TokenManagerTenantRemovalTests.cs`

**Interfaces:**
- Consumes: `ModernWMS.Core` 与 `ModernWMS.WMS` 编译程序集。
- Produces: 对公开成员、方法参数、编译后 SQL/字符串字面量、JWT JSON claim 和装箱 operationKey 的无租户验收门禁。

- [ ] **Step 1: 写入失败的程序集契约测试**

测试必须反射生产类型，拒绝成员名、参数名和编译后字符串字面量匹配 `tenant`；字符串读取复用现有 `PackingTaskQueryServiceTests.ReadStringLiterals` 的 IL 解析方式，不扫描源文件。

- [ ] **Step 2: 写入失败的 JWT 和装箱行为测试**

JWT 测试生成并解析真实 token，断言 JSON claim 只有用户身份字段；装箱测试将 `CurrentTenant()` 改为仅包含真实用户字段，并断言 operationKey 对同一业务命令稳定、对不同任务/明细/分配或动作不同。

- [ ] **Step 3: 验证 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~NoTenantDependencyTests|FullyQualifiedName~TokenManagerTenantRemovalTests|FullyQualifiedName~PackingTaskQueryServiceTests"`

Expected: FAIL，明确报告现有 `CurrentUser.tenant_id`、MultiTenancy 类型或生产 SQL 中的租户字面量。

- [ ] **Step 4: 暂不提交**

保持红灯测试与后续生产改造在同一实施阶段，避免仓库停留在不可构建提交。

### Task 2: 删除身份、登录、API 和实体租户契约

**Files:**
- Delete: `backend/ModernWMS.Core/MultiTenancy/ITenantProvider.cs`
- Delete: `backend/ModernWMS.Core/MultiTenancy/Tenant.cs`
- Delete: `backend/ModernWMS.Core/MultiTenancy/TenantProvider.cs`
- Modify: `backend/ModernWMS.Core/Extentions/StartupExtensions.cs`
- Modify: `backend/ModernWMS.Core/JWT/CurrentUser.cs`
- Modify: `backend/ModernWMS.Core/Controller/AccountController.cs`
- Modify: `backend/ModernWMS.Core/Services/AccountService.cs`
- Modify: `backend/ModernWMS.Core/Models/LoginOutputViewModel.cs`
- Modify: all Core and WMS entity/ViewModel files listed in Appendix A.

**Interfaces:**
- Consumes: 用户 ID、账号、姓名、角色、角色 ID、有效状态。
- Produces: 不含租户的登录结果、JWT claim、DTO、entity 和服务签名。

- [ ] **Step 1: 删除 MultiTenancy 和身份字段**

删除三种 MultiTenancy 类型及 DI；从 CurrentUser、用户、角色、菜单、角色仓库、业务实体与 ViewModel 删除租户属性和默认值。

- [ ] **Step 2: 改造登录 SQL**

使用以下业务关联，不选择或比较租户列：

```sql
FROM wms_user AS user
INNER JOIN wms_userrole AS role
  ON role.role_name = user.user_role
WHERE (user.user_name = @loginName OR user.user_num = @loginName)
  AND (user.auth_string = @md5Password OR user.auth_string = @plainPassword)
LIMIT 1
```

- [ ] **Step 3: 更新登录集成测试并验证 GREEN**

测试表定义和数据删除 tenant 列；继续验证按用户名/工号登录、角色 ID 映射和 SQL 注入无效。

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~Security"`

Expected: PASS；环境未配置开发 MySQL 时数据库特性测试按现有规则跳过，不伪造通过。

### Task 3: 删除通用 CRUD、权限和流水号租户依赖

**Files:**
- Modify: `backend/ModernWMS.Core/Utility/FucntionHelper.cs`
- Modify: WMS 生产代码 Appendix A 中除 StockAllocation、PackingTask、DispatchWorkflow、Dispatchlist、Asn 外的 Services 文件。
- Modify: 对应 Appendix A 测试文件。

**Interfaces:**
- Consumes: 主键、角色 ID、菜单 ID、仓库 ID、货主 ID、库区/库位 ID、有效状态、业务流水表名。
- Produces: 无租户过滤/写入的 CRUD、权限授权和全局流水号。

- [ ] **Step 1: 删除方法参数和匿名对象 tenant 字段**

逐服务删除 `tenantId` 参数、`currentUser.tenant_id`、SELECT/WHERE/JOIN/INSERT/UPDATE 中的租户列以及按租户分组逻辑。

- [ ] **Step 2: 保留真实授权条件**

角色菜单使用 userrole/menu/authority；角色仓库使用 role/warehouse；库区与库位使用 warehouse/warehouse_area/is_valid；货主与 SKU 使用各自主键和有效状态。

- [ ] **Step 3: 改造全局流水号**

`GetFormNoListAsync` 只按 `table_name` 的现有全局序列加锁和递增，INSERT 不写 tenant，调用方不传 tenant。

- [ ] **Step 4: 运行聚焦测试**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~Company|FullyQualifiedName~Freightfee|FullyQualifiedName~GoodsOwner|FullyQualifiedName~Print|FullyQualifiedName~Userrole|FullyQualifiedName~Warehouse|FullyQualifiedName~ActionLog"`

Expected: PASS，且测试 fixture 不再创建或断言 tenant 字段。

### Task 4: 删除库存分配和共享预占租户身份

**Files:**
- Modify: `backend/ModernWMS.WMS/IServices/StockAllocation/IStockAllocationMutationService.cs`
- Modify: `backend/ModernWMS.WMS/IServices/StockAllocation/StockAllocationMutationModels.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockReservationMutationCoordinator.cs`
- Modify: Appendix A 中 Stock、Stockadjust、Stockfreeze、Stockmove、Stockprocess、Stocktaking 和 ASN 服务。
- Create: `backend/ModernWMS.Tests/StockAllocation/NoTenantStockMutationContractTests.cs`

**Interfaces:**
- Consumes: ERP warehouse ID、ERP stock ID、allocation ID、reservation/sourceLineKey、operationKey、数量、版本和操作人。
- Produces: 全局幂等、单一预占来源、无租户库存变更链。

- [ ] **Step 1: 写入库存契约 RED 测试**

通过编译后 SQL 字面量断言共享预占表、库存运行配置、allocation/log/operation 查询与写入不含 tenant；断言 `StockMutationContext` 不含 TenantId。

- [ ] **Step 2: 删除库存 API tenant 参数**

`PrelockAsync` 与 `PrelockReservationOwnersAsync` 删除 tenant 参数；`StockMutationContext` 删除 TenantId；所有调用方同步修改。

- [ ] **Step 3: 以真实键重写 SQL**

运行配置按 `erp_warehouse_id`；allocation 按 `id + erp_stock_id`；共享命令按 `namespace + command_id`；预占头按 `source_system + biz_type + biz_id + deleted`；预占明细按 `reservation_id + source_line_key + stock_id + deleted`；日志和 operation 按全局 operationKey 与 allocation/event。

- [ ] **Step 4: 保留守恒与 CAS**

保留 stock/allocation 数量守恒、owner/location 引用校验、row version、reservation version、command PENDING/SUCCEEDED 和 replay fingerprint。

- [ ] **Step 5: 运行库存相关测试**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~StockAllocation|FullyQualifiedName~Asn|FullyQualifiedName~Domain"`

Expected: PASS。

### Task 5: 删除装箱、出库和调度工作流租户依赖

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- Modify: all DispatchWorkflow and Dispatchlist files in Appendix A.
- Modify: `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`
- Modify: all DispatchWorkflow and Dispatchlist tests that fail compilation or tenant contract.

**Interfaces:**
- Consumes: task/item、任务所属人、库存 order_user_id、warehouse、SKU、selection、reservation、allocation、row version、request/command ID。
- Produces: 无租户装箱列表/库存查询/绑定/减少/取消、拣货、回退、装箱、称重和出库流程。

- [ ] **Step 1: 删除装箱数据源和服务 tenant 上下文**

所有 Page/Selectable/Save/Delete 接口继续接收 CurrentUser 以执行用户与仓库授权，但 CurrentUser 不含租户；SQL 不再选择、过滤、关联或写入 tenant。

- [ ] **Step 2: 重建 operationKey**

使用动作、PACKING_TASK、task ID、item ID、stock/allocation ID、数量、selection/reservation 版本或 command ID生成哈希；同命令稳定，不同业务身份不冲突。

- [ ] **Step 3: 保留 selection 生命周期**

ACTIVE 查询、CANCELLED 审计、TRANSFERRED 转换、取消人/原因/时间、row_version 和 operation_source 全部保留。

- [ ] **Step 4: 改造 DispatchWorkflow/Dispatchlist**

dispatch order、picklist、weighing box 与工作流操作按 order/task/item/warehouse/status/row version 关联；角色菜单和仓库授权不放宽。

- [ ] **Step 5: 运行装箱和出库测试**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~PackingTask|FullyQualifiedName~DispatchWorkflow|FullyQualifiedName~Dispatchlist"`

Expected: PASS。

### Task 6: 删除前端租户字段和请求

**Files:**
- Modify: every Frontend file in Appendix A.
- Create: `frontend/src/utils/http/noTenantRequest.spec.ts`

**Interfaces:**
- Consumes: 登录凭据与真实业务表单。
- Produces: 不读取、保存或发送 tenant 的 API payload 和页面模型。

- [ ] **Step 1: 写入请求 RED 测试**

调用真实 API 参数构造函数或表单提交映射，断言产生的 payload 不含 `tenant_id`/`tenantId`；不得只断言源文本。

- [ ] **Step 2: 删除类型、默认值、示例数据和请求拼装**

删除 Appendix A 前端文件的 tenant 字段和固定 0/1，保持其它业务字段不变。

- [ ] **Step 3: 运行前端测试和类型构建**

Run: `npm run test:unit`

Run: `npm run build`

Workdir: `frontend`

Expected: PASS。

### Task 7: 新增全量物理删列 Flyway 迁移

**Files:**
- Create: `flyway/sql/V20260827090000__remove_tenant_dependencies.sql`
- Create: `backend/ModernWMS.Tests/Database/RemoveTenantMigrationContractTests.cs`
- Do not modify: Appendix A 的历史 Flyway/手工 SQL。

**Interfaces:**
- Consumes: information_schema 已确认的相关 wms_* 与四张 trk_stock_reservation* 表。
- Produces: 无 tenant 列、无 tenant 索引/唯一键的数据库目标结构。

- [ ] **Step 1: 运行只读重复检查**

对所有去掉 tenant 后的唯一业务组合执行 GROUP BY/HAVING，只接受 duplicate_groups=0；记录命令与结果，不执行 DDL/DML。

- [ ] **Step 2: 写入迁移契约 RED 测试**

解析新迁移，断言每个当前 tenant 列都有 `DROP COLUMN tenant_id`，所有含 tenant 的索引先删除并有无 tenant 替代，迁移本身不创建 tenant 列、默认值或兼容列。

- [ ] **Step 3: 写入版本化迁移**

按唯一键替换、普通索引替换、DROP COLUMN 的顺序处理全部相关表。operationKey、reservation 来源、仓库/库区/库位和角色授权使用真实业务键。

- [ ] **Step 4: 静态验证迁移**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --filter "FullyQualifiedName~RemoveTenantMigrationContractTests"`

Expected: PASS。禁止运行 `flyway migrate` 或数据库初始化。

### Task 8: 全仓验收、审查和提交

**Files:**
- Modify only: 本计划任务产生的文件。
- Protect: 用户已有七组改动。

**Interfaces:**
- Consumes: 全部前述阶段。
- Produces: 可构建、可测试、静态扫描无租户生产引用的提交。

- [ ] **Step 1: 全量静态扫描**

Run: `rg -n -i "tenantId|tenant_id|tenant-id|tenant id|MultiTenancy|TenantProvider" backend/ModernWMS.Core backend/ModernWMS.WMS backend/ModernWMS frontend/src`

Expected: no matches。

历史迁移只允许存在于未修改的旧文件；新迁移只允许出现 DROP 目标和验收说明。

- [ ] **Step 2: 后端全量验证**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj`

Run: `dotnet build backend/ModernWMS.sln --no-restore`

Expected: PASS。

- [ ] **Step 3: 前端全量验证**

Run: `npm test`

Workdir: `frontend`

Expected: PASS。

- [ ] **Step 4: 差异审查**

Run: `git diff --check`

Run: `git status --short`

确认没有用户已有文件进入任务差异或暂存区。

- [ ] **Step 5: 分阶段或最终中文提交**

只 git add Appendix A 中实际修改的任务文件、新测试和新迁移；使用清晰中文提交说明，不 push。

---

## Appendix A: 当前租户引用文件清单

### Core/Host

- `backend/ModernWMS.Core/Controller/AccountController.cs`
- `backend/ModernWMS.Core/DBContext/Entities/ErpCommodityMapEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/ErpGoodsOwnerMapEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/ErpReceiptItemEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/ErpReceiptRecordEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/ErpWarehouseOperatorGroupEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsErpStockAllocationEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsErpStockAllocationLogEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsErpStockReservationAllocationEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsInventoryOperationEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsInventoryRuntimeConfigEntity.cs`
- `backend/ModernWMS.Core/DBContext/Entities/WmsStockRecordEntity.cs`
- `backend/ModernWMS.Core/Extentions/StartupExtensions.cs`
- `backend/ModernWMS.Core/JWT/CurrentUser.cs`
- `backend/ModernWMS.Core/Models/GlobalUniqueSerialEntity.cs`
- `backend/ModernWMS.Core/Models/LoginOutputViewModel.cs`
- `backend/ModernWMS.Core/Models/RoleWarehouseEntity.cs`
- `backend/ModernWMS.Core/Models/UserroleEntity.cs`
- `backend/ModernWMS.Core/Models/userEntity.cs`
- `backend/ModernWMS.Core/MultiTenancy/ITenantProvider.cs`
- `backend/ModernWMS.Core/MultiTenancy/Tenant.cs`
- `backend/ModernWMS.Core/MultiTenancy/TenantProvider.cs`
- `backend/ModernWMS.Core/Services/AccountService.cs`
- `backend/ModernWMS.Core/Utility/FucntionHelper.cs`

### WMS 生产代码

- `backend/ModernWMS.WMS/Controllers/user/UserController.cs`
- `backend/ModernWMS.WMS/Entities/Models/ActionLog/ActionLogEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Approve/FlowSetMainEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Asn/AsnEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Asn/AsnmasterEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Asn/AsnsortEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWeighingBoxEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchWorkflowEntities.cs`
- `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchlistEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Freightfee/FreightfeeEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/GoodsOwner/GoodsownerEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Goodslocation/GoodslocationEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/PackingTask/PackingTaskStockSelectionEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Print/PrintSolutionEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Rolemenu/MenuEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Rolemenu/RolemenuEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Sku/SpuEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stock/StockEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stockadjust/StockadjustEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stockfreeze/StockfreezeEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stockmove/StockmoveEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stockprocess/StockprocessEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stockprocess/StockprocessdetailEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Stocktaking/StocktakingEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Warehouse/WarehouseEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Warehousearea/WarehouseareaEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/Warehousearea/WarehouseareaOperatorGroupEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/company/CompanyEntity.cs`
- `backend/ModernWMS.WMS/Entities/Models/supplier/SupplierEntity.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Asn/AsnsortViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Dispatchlist/DispatchlistViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Dispatchlist/PreDispatchlistViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Freightfee/FreightfeeViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Goodslocation/GoodslocationViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Print/PrintSolutionViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stock/StockViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockadjust/StockadjustViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockfreeze/StockfreezeViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockmove/StockmoveViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockprocess/StockprocessGetViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockprocess/StockprocessViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockprocess/StockprocessWithDetailViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Stockprocess/StockprocessdetailViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/User/UserExcelImportViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/User/UserViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Warehouse/WarehouseViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/Warehousearea/WarehouseareaViewModel.cs`
- `backend/ModernWMS.WMS/Entities/ViewModels/userrole/UserroleViewModel.cs`
- `backend/ModernWMS.WMS/IServices/Sku/ISpuService.cs`
- `backend/ModernWMS.WMS/IServices/StockAllocation/IStockAllocationMutationService.cs`
- `backend/ModernWMS.WMS/IServices/StockAllocation/StockAllocationMutationModels.cs`
- `backend/ModernWMS.WMS/IServices/user/IUserService.cs`
- `backend/ModernWMS.WMS/Services/ActionLog/ActionLogService.cs`
- `backend/ModernWMS.WMS/Services/Asn/AsnService.cs`
- `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.Allocations.cs`
- `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.CanonicalInventory.cs`
- `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.Details.cs`
- `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.Inventory.cs`
- `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchOrderQueryService.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Carrier.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Creation.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.PackingPlan.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Picking.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Reconciliation.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Rollback.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.SourceAdjudication.cs`
- `backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.cs`
- `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistPickingService.cs`
- `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistService.cs`
- `backend/ModernWMS.WMS/Services/FbaShipment/FbaShipmentService.cs`
- `backend/ModernWMS.WMS/Services/Freightfee/FreightfeeService.cs`
- `backend/ModernWMS.WMS/Services/GoodsOwner/GoodsownerService.cs`
- `backend/ModernWMS.WMS/Services/Goodslocation/GoodslocationService.cs`
- `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- `backend/ModernWMS.WMS/Services/Print/PrintSolutionService.cs`
- `backend/ModernWMS.WMS/Services/Rolemenu/RolemenuService.cs`
- `backend/ModernWMS.WMS/Services/Sku/SpuService.cs`
- `backend/ModernWMS.WMS/Services/Stock/StockService.cs`
- `backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs`
- `backend/ModernWMS.WMS/Services/StockAllocation/StockReservationMutationCoordinator.cs`
- `backend/ModernWMS.WMS/Services/Stockadjust/StockadjustService.cs`
- `backend/ModernWMS.WMS/Services/Stockfreeze/StockfreezeService.cs`
- `backend/ModernWMS.WMS/Services/Stockmove/StockmoveService.cs`
- `backend/ModernWMS.WMS/Services/Stockprocess/StockprocessService.cs`
- `backend/ModernWMS.WMS/Services/Stocktaking/StocktakingService.cs`
- `backend/ModernWMS.WMS/Services/Warehouse/WarehouseAccessService.cs`
- `backend/ModernWMS.WMS/Services/Warehouse/WarehouseService.cs`
- `backend/ModernWMS.WMS/Services/Warehousearea/WarehouseareaService.cs`
- `backend/ModernWMS.WMS/Services/company/CompanyService.cs`
- `backend/ModernWMS.WMS/Services/user/UserService.cs`
- `backend/ModernWMS.WMS/Services/userrole/UserroleService.cs`

### 现有测试

- `backend/ModernWMS.Tests/ActionLog/ActionLogServiceMySqlIntegrationTests.cs`
- `backend/ModernWMS.Tests/Company/CompanyServiceTests.cs`
- `backend/ModernWMS.Tests/Freightfee/FreightfeeServiceTests.cs`
- `backend/ModernWMS.Tests/GoodsOwner/GoodsownerServiceTests.cs`
- `backend/ModernWMS.Tests/PackingTask/PackingTaskQueryServiceTests.cs`
- `backend/ModernWMS.Tests/Print/PrintSolutionServiceTests.cs`
- `backend/ModernWMS.Tests/Security/AccountServiceMySqlIntegrationTests.cs`
- `backend/ModernWMS.Tests/Userrole/UserroleServiceTests.cs`
- `backend/ModernWMS.Tests/Warehouse/WarehouseAccessServiceTests.cs`

### 前端

- `frontend/src/types/System/Form.ts`
- `frontend/src/types/WMS/StockAsn.ts`
- `frontend/src/view/base/print/add-or-update-print.vue`
- `frontend/src/view/base/print/print.vue`
- `frontend/src/view/vwms/data/data.ts`
- `frontend/src/view/vwms/types/types.ts`
- `frontend/src/view/warehouseWorking/warehouseFreeze/add-or-update-freeze.vue`
- `frontend/src/view/warehouseWorking/warehouseFreeze/warehouseFreeze.vue`
- `frontend/src/view/warehouseWorking/warehouseProcessing/add-or-update-process.vue`
- `frontend/tests/smoke/delivery-management.spec.ts`
- `frontend/tests/smoke/navigation.spec.ts`
- `frontend/tests/smoke/print-and-code.spec.ts`
- `frontend/tests/smoke/table-workflows.spec.ts`

### 历史迁移与手工脚本（只读基线，禁止修改）

- `flyway/manual/erp_stock_allocation_cutover.sql`
- `flyway/manual/erp_stock_reservation_cutover_local_320118.sql`
- `flyway/manual/erp_stock_reservation_cutover_local_320118_v2.sql`
- `flyway/sql/V1__baseline_wms_schema.sql`
- `flyway/sql/V20260820210000__erp_stock_allocation_contract.sql`
- `flyway/sql/V20260821001000__stock_reservation_allocation.sql`
- `flyway/sql/V20260821103500__optional_receipt_storage_hierarchy.sql`
- `flyway/sql/V20260826100000__packing_selection_lifecycle.sql`
- `flyway/sql/V2__seed_wms_identity.sql`
