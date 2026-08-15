# 装箱任务只读查询

## 范围

发货管理首个 Tab 可通过前后端双 feature flag 从旧 FBA 准备单切换为正式装箱任务只读列表。该查询直接读取共享 MySQL 中由调度端维护的：

- `ruiyi_sellfox_packing_task`
- `ruiyi_sellfox_packing_task_item`

ModernWMS 不写上述表，不生成 `trk_stock_move`、FBA 主标识或 WMS 发货单，也不改变后续待拣货至已出库五个 Tab 的状态机。

## 门禁

- 后端：`Features:PackingTaskFirstStep=true`。
- 前端：`VITE_PACKING_TASK_FIRST_STEP_ENABLED=true`。
- 第一阶段不按仓库或租户仓库绑定筛选，返回全部仓库的有效装箱任务。
- 数据仍过滤 `source_canceled=0`、`source_deleted=0`，撤销或已从来源消失的任务不进入操作列表。

## 查询契约

- 排序：`source_create_time DESC, id DESC`。
- 先分页主表，再一次性批量加载未软删除明细，避免 N+1。
- 搜索范围仅为装箱任务号、商品名、SKU、FNSKU、MSKU。
- `complete_num`、主/明细 `task_num`、`quantity_shipped`、`stock_available` 等源数量保持 nullable；前端缺失值显示为空，显式 `0` 显示为 `0`。

## 验证与剩余门禁

单元测试覆盖功能关闭、全仓查询、撤销/删除过滤、稳定排序、明细软删除、搜索和 nullable 传播。当前页面是授权用户可见的全仓只读视图；后续引入仓库筛选或数据权限时，必须重新明确 SellFox 仓库 ID 与 WMS/ERP 仓库绑定关系。
