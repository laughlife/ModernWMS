# 方案 A 数据库迁移与后续清理清单

## 执行边界

- 正向迁移：`flyway/sql/V20260829120000__direct_erp_stock_packing.sql`
- 人工回退：`flyway/manual/rollback_direct_erp_stock_packing_20260829.sql`
- 本次交付只准备、校验脚本，不对生产库执行任何写入。
- 正向迁移把运行模式表改名保留，不立即删除数据；回退必须与方案 A 之前的应用版本同时执行。

## 方案 A 继续使用的核心表

| 表 | 用途 | 结论 |
| --- | --- | --- |
| `trk_stock` | ERP 库存余额唯一事实表，候选、绑定、预占、调整、换绑、取消、释放和出库均按 `id` 操作 | 必须保留 |
| `trk_stock_record` | ERP 库存余额变动流水 | 必须保留 |
| `trk_stock_reservation` | 共享预占主表 | 必须保留 |
| `trk_stock_reservation_item` | 共享预占明细与幂等身份 | 必须保留 |
| `trk_stock_reservation_command` | 共享预占命令与幂等结果 | 必须保留 |
| `trk_stock_reservation_command_item` | 共享预占命令明细 | 必须保留 |
| `wms_packing_task_stock_selection` | 装箱任务到 `erp_stock_id` 的选择关系 | 必须保留 |
| `wms_dispatch_order`、`wms_dispatch_packing_task`、`wms_dispatch_packing_task_item` | WMS 出库单、装箱任务及商品明细 | 必须保留 |
| `wms_dispatchpicklist` | 出库拣货与预占身份承接表 | 必须保留 |
| `wms_weighing_box`、`wms_weighing_box_item` | 实际装箱箱体及商品到 `erp_stock_id` 的关系 | 必须保留 |
| `wms_dispatch_workflow_operation` | 出库命令幂等与结果账本 | 必须保留 |
| `wms_action_log` | 用户绑定、换绑、取消等操作日志 | 必须保留 |

`wms_packing_task_stock_selection`、`wms_dispatchpicklist`、`wms_weighing_box_item` 中原有的 WMS SKU、库存、货主、库位和位置分配字段只用于历史兼容；迁移后允许 `NULL`，方案 A 新增行不再写入伪造的 `0` 身份。

## 后续可删除表

| 表 | 当前处理 | 可删除条件 |
| --- | --- | --- |
| `wms_inventory_runtime_config_retired_20260829` | 正向迁移由 `wms_inventory_runtime_config` 改名而来；应用代码已无实体、查询或写入依赖 | 新版本稳定运行且回退观察期结束后，可单独审批 `DROP TABLE` |

这是本次代码审计后唯一已经确认可进入后续删除清单的整表。不要在本次正向迁移中直接删除，否则会破坏可回退性。

## 暂时不得删除的历史兼容表

| 表 | 仍有依赖 | 后续清理前置条件 |
| --- | --- | --- |
| `wms_erp_stock_allocation` | 历史选择、拣货和库存分配仍需读取或结算 | 历史 `stock_allocation_id` 全部清零/归档，历史释放适配器下线 |
| `wms_erp_stock_reservation_allocation` | 历史预占与位置分配映射仍被结算逻辑使用 | 历史预占全部完结并完成一致性核对 |
| `wms_erp_stock_allocation_log` | 历史位置分配变更审计仍被库存分配服务使用 | 审计保留期结束，相关服务下线或改为归档查询 |
| `wms_inventory_operation` | 库存分配服务的操作幂等账本仍在读写 | 对应旧库存操作入口和服务全部下线 |

## 当前仍在使用、不得误删的旧 WMS 表

- `wms_stock`、`wms_stock_record`：收货、上架、移库、冻结、盘点等非本次装箱出库流程仍有生产代码依赖。
- 仓库、库区、库位、货主及 SKU 映射相关表：仓库维护和其他库存业务仍在使用；方案 A 只是让装箱库存选择和出库库存变更脱离这些身份。

因此，“第二套仓库业务编号依赖”和“位置分配依赖”的删除范围是本次装箱/出库业务链路，不能据此直接删除仍承载其他 WMS 功能的整表。

## 后续清理核验 SQL（只读）

正式申请删除历史兼容表前，至少核验以下计数为零，并结合日志保留策略复核：

```sql
SELECT COUNT(*) AS active_legacy_selection
FROM wms_packing_task_stock_selection
WHERE status = 'ACTIVE' AND stock_allocation_id IS NOT NULL;

SELECT COUNT(*) AS active_legacy_pick
FROM wms_dispatchpicklist
WHERE is_update_stock = 0 AND stock_allocation_id IS NOT NULL;

SELECT COUNT(*) AS open_legacy_allocation
FROM wms_erp_stock_allocation
WHERE allocated_qty > 0 OR occupied_qty > 0;

SELECT COUNT(*) AS open_legacy_reservation_allocation
FROM wms_erp_stock_reservation_allocation
WHERE remaining_qty > 0 AND deleted = b'0';
```

清理审批时仍需在目标数据库执行 `SHOW CREATE TABLE` 复核最终结构；以上 SQL 仅作为只读清理评审模板，不属于自动迁移。
