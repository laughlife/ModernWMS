# ERP 与 ModernWMS 库存预占所有权设计

## 目标

在不新增第二库存余额的前提下，为 `trk_stock.occupied_qty` 建立可追踪、可部分释放、可正式消费的业务所有权。任何锁定、解锁和已锁定出库都必须对应稳定的业务来源、预占明细和幂等命令；历史无来源占用不得伪造成合法锁定。

本设计仅用于本机测试环境。实施期间不新增、不运行任何测试，不运行构建；验证只使用静态代码核对、只读数据库对账和用户授权后的人工业务验收。

## 共同事实与表归属

`trk_stock` 是唯一库存余额，`trk_stock_record` 是唯一数量事件流水。ModernWMS 的 allocation 与 reservation allocation 只分解 ERP 余额和占用，不形成独立库存。

Ruoyi 拥有：

- `trk_stock_reservation`：业务预占主单。
- `trk_stock_reservation_item`：ERP stock 维度的占用所有权明细。
- `trk_stock_reservation_command`：跨系统共享幂等命令头。
- `trk_stock_reservation_command_item`：不可变命令输入与逐行结果。
- `trk_stock_record` 的 command、reservation、reservation item、action 引用。

ModernWMS 拥有：

- `wms_erp_stock_reservation_allocation`：reservation item 的库位持有分解。
- `wms_inventory_operation` 的 shared command 与 reservation 引用。
- `wms_erp_stock_allocation_log` 的 shared command 与 reservation 引用。

## 数量不变量

- 有效 reservation item 的 `remaining_qty` 合计必须等于 `trk_stock.occupied_qty`。
- allocation reservation 的 `remaining_qty` 合计必须等于 allocation 的 `occupied_qty`。
- 同一 reservation item 的 allocation reservation 剩余合计必须等于 item 的 `remaining_qty`。
- 每行必须满足 `reserved_qty = released_qty + consumed_qty + remaining_qty`，四个数量均非负。
- `RELEASE`：ERP `occupied` 减少、`available` 增加、`total` 不变；allocation 只减少 `occupied`。
- `CONSUME`：ERP `occupied` 与 `total` 同减、`available` 不变；allocation 的 `allocated` 与 `occupied` 同减。

## 预占状态机

动作包括 `RESERVE`、`RESERVE_MORE`、`PARTIAL_RELEASE`、`RELEASE_ALL`、`PARTIAL_CONSUME`、`CONSUME_ALL`、`CLAIM_ORPHAN` 和 `RECONCILE_ORPHAN_RELEASE`。

主单状态包括 `ACTIVE`、`PARTIALLY_SETTLED`、`RELEASED`、`CONSUMED`、`MIXED_CLOSED` 和 `ORPHANED`。已消费数量不能解锁；退回必须创建引用原消费业务的新 `RETURN/RECEIVE` 补偿流水。

外部与 FBA 出库在实际出库确认时直接消费锁定量，禁止先释放再扣减。内部跨仓在尚无独立在途资产余额时保持来源 reservation，目标仓物理收货事务内执行来源消费和目标收货。

## 共享命令与幂等

共享命令唯一键为 `(tenant_id, namespace, command_id)`，不包含删除标记且永不复用。命令主表保存 action、reservation、请求指纹和 `PENDING/SUCCEEDED` 状态；命令明细逐行保存 reservation item、stock、数量、期望版本、allocation plan 指纹和结果引用。

同键同指纹且已成功时返回原结果；同键不同指纹时拒绝。失败事务整体回滚，不持久化伪成功。ModernWMS 的 `wms_inventory_operation` 仅记录库位侧执行结果，不再承担跨系统命令所有权。

## 固定锁序

1. 业务主单或状态行。
2. WMS 管理仓 runtime config，按 tenant 与 ERP warehouse 升序使用共享锁；维护切换使用排他锁。
3. reservation 主单。
4. reservation items 按 item ID、stock ID 升序。
5. 预先创建或回读目标 POOL，收齐 source 与 target stock 后按 stock ID 全局升序。
6. allocation 按 allocation ID 升序。
7. allocation reservation 按 ID 升序。
8. shared command；WMS 同时认领本地 inventory operation。
9. 更新持有、余额与库位分配，写 ERP 流水和 WMS 审计，完成命令。

事务内禁止 HTTP、Redis、通知和物流外呼。WMS 管理仓由 ModernWMS 协调共享事务，非 WMS 仓由 Ruoyi 协调；同一 runtime 模式只允许一个库存写协调者。

## 业务入口收口

Ruoyi 的调度、FBA 和全部 `LOCK/UNLOCK/SHIP_OUT_LOCKED` 必须携带 shared command 与 reservation item。FBA 使用永久明细持有，追加和替换使用 reservation 版本 CAS；`ship_count` 只在成功消费后更新。

ModernWMS 的装箱选择、拣货、出库、装箱缩量、来源取消、回滚、对账、冻结/解冻以及涉及占用的调整、盘点、加工和移库全部使用 reservation。收货只增加 available 与 total，不创建 reservation。Canonical 模式下缺少 reservation 的占用变化必须拒绝。

## 历史占用重建

本机当前 ERP 占用为 2950。重建必须逐 stock 联合 ERP 调度/FBA业务、旧新流水、WMS装箱选择、待出库拣货、冻结及其他稳定业务证据。只有业务主单、永久明细、stock、数量和未结算状态唯一匹配时才能认领。

禁止按 SKU、相同数量、最新记录、名称或近似 JSON 猜测来源。无法唯一解释的数量进入 ORPHANED 隔离，保存证据 JSON 与 hash。ORPHANED 只能通过 `CLAIM_ORPHAN` 认领或通过 `RECONCILE_ORPHAN_RELEASE` 在同一事务审计释放，禁止直接更新库存数量。切换前 ORPHANED 剩余必须为零。

## 发布与回滚边界

先发布双方加法 DDL 与兼容代码，保持旧模式；维护窗口停写后执行历史重建、ORPHANED 处理、allocation 与 reservation 回填及逐 stock/逐库位守恒。最后同步启用 ERP 管理仓门禁与 WMS `CANONICAL_ERP`。

首笔新 reservation 交易前可以回退兼容代码和新增结构；首笔交易后不得回到无 owner 的 occupied 或旧双库存逻辑，只能依据 shared command、ERP流水和WMS审计向前修复。

## 联合契约来源

- 最终 Memory：`62410b3f-f85a-41d9-b692-dc9e57bfba80`
- Ruoyi评审：`94946700-0b44-4c4b-aa9e-5963dc7b5777`
- ModernWMS复核：`5b253300-40d8-414e-aa96-c31787386c99`

