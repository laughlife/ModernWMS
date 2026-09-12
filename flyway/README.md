# ModernWMS Flyway 使用说明

仓库固定使用 Flyway `11.15.0`。迁移脚本不会联网下载工具，也不会接受其他版本，避免开发机 PATH 中的漂移版本静默改变数据库行为。

从 Flyway 官方发行渠道取得 `11.15.0`，在本机完成来源和校验和核对后解压到仓库外部目录。通过以下任一方式指定其 `flyway.cmd`：

```powershell
$env:MODERNWMS_FLYWAY_PATH = 'C:\Tools\flyway-11.15.0\flyway.cmd'
# 或每次调用传入 -FlywayPath
```

不要把 Flyway 二进制、数据库凭据或本地绝对路径提交到仓库。项目策略固定为：

- 历史表：`wms_flyway_schema_history`
- `cleanDisabled=true`
- `baselineOnMigrate=false`
- `outOfOrder=false`
- 只允许回环地址上的本机开发库，且每次必须显式传入 `-ConfirmDevelopmentDatabase`
- 普通运行只执行 `info` 和 `validate`
- 只有显式传入 `-Apply` 才执行 `migrate`
- 空库通过 `V1__baseline_wms_schema.sql` 创建 50 张 WMS 自有表；不创建或修改 ERP 表

本脚本不提供生产数据库或远程数据库的绕过开关。生产迁移必须单独设计、评审并授权，不能用本机开发脚本执行。

## 两种首次接入方式

空的本机开发库在备份并确认连接后执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 `
  -ConfirmDevelopmentDatabase -Apply
```

已有 50 张 WMS 表的本机开发库只能执行一次显式基线登记：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 `
  -ConfirmDevelopmentDatabase `
  -BaselineExisting `
  -ConfirmExistingSchemaFingerprint 'WMS_SCHEMA_MATCHES_V1'
```

这不是跳过检查：脚本先读取 `INFORMATION_SCHEMA` 和每张表的 `SHOW CREATE TABLE`，与 `flyway/wms-baseline-manifest.json` 的 50 张表结构指纹逐一比较。只有全部一致才执行 Flyway `baseline`，在 `wms_flyway_schema_history` 登记版本 1；任一表缺失、多出或结构不同都会拒绝写入。该流程不会复制数据，也不会读取或校验 ERP 表。执行前仍必须备份数据库。

脚本没有远程/生产绕过参数，并同时要求主机是回环地址、库名严格等于 `ruoyi-vue-pro`。日常启动不会调用上述命令。

版本 2 仅按主键补充 `SeedData` 中确定的 WMS 角色与菜单授权基线；已有同 ID 记录保持不变。
该数据迁移只写入 `wms_userrole`、`wms_menu` 和 `wms_rolemenu`，不访问 ERP 表，也不会固化默认管理员账号、密码哈希或邮箱。新环境的首个管理员必须通过独立的一次性配置流程显式创建。

## 库存标识改名（2026-09-12）

`V20260912120000__rename_erp_stock_contract_to_trk_stock.sql` 将三张
`wms_erp_stock_*` 表改为对应 `wms_trk_stock_*`，并将明确列出的 13 张 WMS 表中的
`erp_stock_id` / `erp_stock_record_id` 改为 `trk_stock_id` / `trk_stock_record_id`。
使用 MySQL 8 的 `RENAME COLUMN` 保留类型、默认值、注释、索引及约束引用；索引和约束自身的历史名称保留。
该迁移没有库存数量或预占 DML，不改名或删除 `trk_stock`、`trk_stock_record`、`trk_stock_reservation*`。

执行顺序：

1. 完成 ModernWMS 前后端、ruoyi-vue-pro 及其调用方的契约对齐，确认两边可同时发布。数据库物理改名由本迁移统一负责，ERP 不得再次执行同一 DDL。
2. 先在开发库只读核对版本、13 张表的旧列和三张旧表均存在、新列/新表尚不存在，以及历史 Flyway 版本已齐备；检查视图、存储过程等数据库对象是否仍引用旧名。备份涉及表的结构和数据，并记录库存/预占摘要。
3. 由环境负责人安排暂停相关读写，执行迁移并协调切换两边应用。核对结构、行数及库存/预占摘要；测试与业务验收由真人执行。
4. 开发环境完成后，生产库重复相同的核对、备份、迁移与应用切换顺序。不要通过本仓库的本机开发脚本绕过生产限制。

MySQL DDL 隐式提交；失败可能留下部分完成状态，禁止直接整份重跑。先核对已完成的语句，再决定补齐或回退。
完整执行后的逆向 DDL 位于 `manual/rollback_trk_stock_naming_20260912.sql`，须与应用版本回退一起安排；部分完成时只逆转已完成的语句。
历史版本迁移与历史手工切换脚本保留原文，不能作为改名后环境的日常操作脚本再次执行。
