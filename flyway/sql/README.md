# Flyway SQL migrations

本目录只存放经过审查的 ModernWMS 结构迁移。普通应用启动不会读取或执行这里的文件。

新增迁移使用不可变的版本化文件名：

```text
V20260818000100__add_example_table.sql
```

- 已在任何环境执行过的版本文件不得修改、覆盖或重新排序；后续修正必须新增版本。
- 默认只允许管理 `wms_*` 自有表。修改共享 ERP 表必须先明确核对 ERP 写入端、WMS 消费端和发布顺序。
- SQL 必须可审查、可重复部署；执行前备份目标数据库，并先在一次性测试库验证。
- 不在本目录保存账号、密码、连接地址或生产数据导出。

`V1__baseline_wms_schema.sql` 是可执行的空库结构基线，只包含 50 张 `wms_*` 自有业务表，不包含 ERP 表、数据、凭据、旧 EF 历史表或 Flyway 历史表。它来自已授权的本机开发库物理结构，已移除数据相关的 `AUTO_INCREMENT` 计数。

- 空开发库：显式执行 `Update-Database.ps1 -Apply`，Flyway 会执行 V1 创建 WMS 结构。
- 已有开发库：不得执行 V1；使用 README 中的一次性 `-BaselineExisting` 流程。脚本会逐表比较 `SHOW CREATE TABLE` 的 SHA-256 指纹，完全匹配后才记录版本 1。
- V1 一旦在任何库执行或登记，不得修改。以后所有结构变化新增 `V...__description.sql`。
