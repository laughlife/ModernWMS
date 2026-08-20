# ERP 与 ModernWMS 库存预占所有权 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可追踪的库存预占所有权，重建本机历史占用并安全恢复唯一库存模式下的签收入库。

**Architecture:** Ruoyi拥有ERP reservation、command和数量流水引用；ModernWMS拥有库位reservation与位置审计引用。双方通过共享数据库同事务维护唯一 ERP 余额、预占所有权和库位分解，并在维护窗口完成历史重建和同步切换。

**Tech Stack:** Java/Spring Boot/MyBatis、ASP.NET Core/C#/Dapper/MySqlConnector、MySQL 8.4、Flyway 11.15.0、Vue 3。

**Spec:** `docs/superpowers/specs/2026-08-21-stock-reservation-ownership-design.md`

## Global Constraints

- 仅操作本机测试环境和本机数据库 `127.0.0.1:3306/ruoyi-vue-pro`。
- 禁止新增或运行任何测试；禁止运行构建、编译、API自动验证或E2E。
- 不擅自启动、停止或重启服务。
- 数据库结构和数据写入必须串行；执行前备份并核对明确目标。
- `trk_stock` 始终是唯一余额，禁止恢复 `wms_stock` 双写。
- 双方门禁必须同步切换，禁止单边启用。

---

### Task 1: Ruoyi共享reservation契约与Mutation收口

**Files:**
- Ruoyi create: `sql/mysql/erp_stock_reservation_contract_20260821.sql`
- Ruoyi modify: `yudao-module-erp/.../LogisticsProviderStockMutationService.java`
- Ruoyi modify: `yudao-module-erp/.../TrkStockRecordDO.java`
- Ruoyi modify: `yudao-module-erp/.../TrkStockMapper.java`
- Ruoyi modify: 调度与FBA库存调用服务

**Interfaces:**
- Produces: reservation master/item/command/command item共享表；强制reservation的ERP Mutation接口；ERP流水reservation引用。

- [ ] 读取最终 Memory `62410b3f-f85a-41d9-b692-dc9e57bfba80` 并锁定表名、字段、状态和动作常量。
- [ ] 编写只含加法结构的 Ruoyi DDL，包含数量CHECK、命令唯一键、来源唯一键和必要索引。
- [ ] 将LOCK、UNLOCK、SHIP_OUT_LOCKED接口改为必须携带command与reservation item，移除stock+qty旁路。
- [ ] 改造调度来源明细为稳定reservation item，并保持内部跨仓消费时点。
- [ ] 改造FBA永久明细、版本CAS和统一升序锁，确保ship_count只由CONSUME更新。
- [ ] 使用`rg`、`git diff --check`和调用链清单静态证明无旧occupied旁路，不运行测试或构建。
- [ ] 只提交Ruoyi本任务文件并写跨项目Memory回执。

### Task 2: ModernWMS库位reservation加法结构

**Files:**
- Modify: `flyway/sql/V20260820210000__erp_stock_allocation_contract.sql` 或新增更高时间戳Flyway迁移（已应用版本不得改写，实际实施必须新增版本）。
- Create: `backend/ModernWMS.Core/DBContext/Entities/WmsErpStockReservationAllocationEntity.cs`
- Modify: `backend/ModernWMS.Core/DBContext/Entities/WmsInventoryOperationEntity.cs`
- Modify: `backend/ModernWMS.Core/DBContext/Entities/WmsErpStockAllocationLogEntity.cs`

**Interfaces:**
- Consumes: Ruoyi reservation/command主键。
- Produces: `wms_erp_stock_reservation_allocation`及WMS审计逻辑引用。

- [ ] 新增Flyway时间戳迁移，创建库位reservation表并追加shared command/reservation引用。
- [ ] 增加reserved/released/consumed/remaining非负与守恒CHECK及唯一键。
- [ ] 增加Core实体映射，保持跨所有者逻辑引用且不创建物理FK。
- [ ] 使用`git diff --check`与DDL静态扫描核对无DML、无旧表写入、无已应用迁移改写。

### Task 3: ModernWMS统一reservation Mutation

**Files:**
- Modify: `backend/ModernWMS.WMS/IServices/StockAllocation/IStockAllocationMutationService.cs`
- Modify: `backend/ModernWMS.WMS/IServices/StockAllocation/StockAllocationMutationModels.cs`
- Modify: `backend/ModernWMS.WMS/Services/StockAllocation/StockAllocationMutationService.cs`
- Create: focused reservation models/services only when单一职责不能放入Mutation owner。

**Interfaces:**
- Consumes: shared command、reservation、reservation item及allocation plan。
- Produces: Reserve/Release/Consume命令结果和重放结果。

- [ ] 将Reserve/Release/ShipLocked签名改为显式reservation command与item输入。
- [ ] 按业务→runtime→reservation→stock→allocation→command固定锁序实现批量预锁。
- [ ] 在同一事务更新reservation item、ERP余额、allocation reservation、allocation余额和两侧审计。
- [ ] 实现同命令同指纹重放、不同指纹拒绝及版本CAS。
- [ ] 增加逐stock、逐allocation和逐item守恒门禁。
- [ ] 使用`rg`和`git diff --check`静态核对接口实现与旧旁路清零。

### Task 4: ModernWMS全部占用业务迁移

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/PackingTask/PackingTaskQueryService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistService.cs`
- Modify: `backend/ModernWMS.WMS/Services/DispatchWorkflow/*.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stockfreeze/StockfreezeService.cs`
- Modify: 涉及occupied的Stockadjust/Stocktaking/Stockprocess/Stockmove服务。

**Interfaces:**
- Consumes: Task 3的reservation Mutation接口。
- Produces: 稳定业务来源键、command ID与部分释放/消费语义。

- [ ] 为装箱选择与缩量建立PACKING reservation主单和永久明细来源键。
- [ ] 为拣货、取消、回滚、对账和正式出库建立OUTBOUND reservation生命周期。
- [ ] 将冻结/解冻迁入统一reservation，保留source_freeze_id仅作来源引用。
- [ ] 将所有涉及occupied的调整、盘点、加工和移库接入统一门禁。
- [ ] 扫描Canonical分支，任何无reservation的Reserve/Release/ShipLocked均必须不存在或明确拒绝。

### Task 5: 双方静态契约复核与兼容提交

**Files:**
- Review: 双方本任务diff、DDL、Mutation接口和业务调用链。

**Interfaces:**
- Consumes: Task 1-4提交与Memory回执。
- Produces: 可执行DDL顺序和无旁路结论。

- [ ] 对齐双方字段类型、状态、action、命令唯一键和锁序。
- [ ] 核对Ruoyi非WMS仓不依赖WMS表，WMS管理仓由ModernWMS协调共享事务。
- [ ] 核对所有外呼在事务提交后执行。
- [ ] 仅运行静态检查，不运行测试或构建；问题返回对应项目修正后重新复核。

### Task 6: 本机加法DDL与历史占用只读重建

**Files:**
- Create: `flyway/manual/erp_stock_reservation_reconcile.sql`
- Use: 双方加法DDL与项目既定迁移入口。

**Interfaces:**
- Consumes: 已复核的双方DDL。
- Produces: candidate claim、逐stock reconcile和ORPHANED清单。

- [ ] 对本机数据库及涉及表做完整备份并核对文件非空。
- [ ] 串行执行Ruoyi加法DDL和ModernWMS Flyway迁移，保持runtime不切换。
- [ ] 只读生成ERP调度/FBA、WMS装箱/拣货/冻结候选来源及证据hash。
- [ ] 逐stock输出occupied、confirmed claim、unexplained和overclaimed，禁止全库总量抵消。
- [ ] 把无法唯一匹配的数量列入ORPHANED隔离输入，不修改库存。

### Task 7: 维护窗口回填、ORPHANED处理与同步切换

**Files:**
- Use: `flyway/manual/erp_stock_allocation_cutover.sql`
- Use: `flyway/manual/erp_stock_reservation_reconcile.sql`

**Interfaces:**
- Consumes: Task 6历史重建结果。
- Produces: reservation、allocation reservation及全量守恒状态。

- [ ] 获得明确维护窗口授权后锁定runtime并停止本机库存写。
- [ ] 认领唯一证据来源；对确认无来源项执行幂等RECONCILE_ORPHAN_RELEASE，禁止直接UPDATE。
- [ ] 回填ERP reservation、WMS allocation及allocation reservation，不增加ERP总库存。
- [ ] 逐stock、逐allocation、逐item核对三组remaining守恒且ORPHANED为0。
- [ ] 同步启用ERP管理仓门禁和WMS CANONICAL_ERP；任一门禁失败则保持维护状态。

### Task 8: 签收入库流程收尾

**Files:**
- Modify: `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.cs`
- Modify: `backend/ModernWMS.WMS/Services/Asn/ErpPendingReceiptService.CanonicalInventory.cs`

**Interfaces:**
- Consumes: 已切换的唯一库存、allocation和shared command契约。
- Produces: 人工实际收货数量写入ERP唯一余额与库位分配。

- [ ] 核对收货只增加available/total且不创建reservation。
- [ ] 核对收货allocation与ERP数量流水同事务并关联实际收货明细。
- [ ] 对PO2608100031执行用户授权的人工页面验收；不运行自动化测试或接口脚本。
- [ ] 写最终跨项目Memory，记录DDL、数据影响、守恒结果、提交和回滚边界。
