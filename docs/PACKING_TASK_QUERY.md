# 装箱任务只读查询

## 范围

发货管理首个 Tab 可通过前后端双 feature flag 从旧 FBA 准备单切换为正式装箱任务只读列表。该查询直接读取共享 MySQL 中由调度端维护的：

- `ruiyi_sellfox_packing_task`
- `ruiyi_sellfox_packing_task_item`

ModernWMS 不写上述表，不生成 `trk_stock_move`、FBA 主标识或 WMS 发货单，也不改变后续待拣货至已出库五个 Tab 的状态机。

## 门禁

- 后端：`Features:PackingTaskFirstStep=false`（默认关闭）。
- 前端：`VITE_PACKING_TASK_FIRST_STEP_ENABLED=false`（默认关闭）。
- 启用后，服务端要求 ERP 仓库 `320118` 存在且未删除，并要求当前租户恰有一条 `erp_warehouse_id=320118` 且 `is_valid=1` 的 WMS 仓库绑定；否则返回可诊断的未就绪错误且不返回任务数据。
- 数据固定过滤仓库 `320118`、`source_canceled=0`、`source_deleted=0`。

## 查询契约

- 排序：`source_create_time DESC, id DESC`。
- 先分页主表，再一次性批量加载未软删除明细，避免 N+1。
- 搜索范围仅为装箱任务号、商品名、SKU、FNSKU、MSKU。
- `complete_num`、主/明细 `task_num`、`quantity_shipped`、`stock_available` 等源数量保持 nullable；前端缺失值显示为空，显式 `0` 显示为 `0`。

## 验证与剩余门禁

单元测试覆盖默认关闭、仓库 readiness fail-closed、过滤、稳定排序、明细软删除、搜索和 nullable 传播。正式启用前仍需用脱敏生产只读证据确认 SellFox `warehouse_id=320118` 与 ERP 仓库 ID 处于同一语义域；未确认时两个 flag 必须保持关闭。
