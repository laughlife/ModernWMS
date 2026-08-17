# MySQL 数据库与初始化

## 目标环境

- MySQL 8.4
- 地址：`127.0.0.1:3306`
- 数据库：`ruoyi-vue-pro`（与 Ruoyi/ERP 共用）
- 字符集：`utf8mb4`

ModernWMS 只维护一个数据库连接。WMS 自有表统一使用 `wms_` 前缀，Ruoyi/ERP 原有表保持原名。运行期数据访问使用 MySqlConnector 与 Dapper；结构变更通过显式 Flyway 命令管理，普通应用启动不执行 DDL。

仓库不保存真实数据库密码。开发机使用 .NET User Secrets，部署环境使用受保护的环境变量或密钥管理服务。

## 初始化方式

应用启动不再自动迁移数据库。空开发库使用 Flyway V1 基线显式创建 WMS 自有结构：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 -ConfirmDevelopmentDatabase -Apply
```

Flyway 历史表为 `wms_flyway_schema_history`。旧 `wms_ef_migrations_history` 不属于 V1 基线，不再用于新结构发布。

V1 只创建结构，不复制开发库数据，也不创建管理员、菜单等基础数据。需要全新可登录环境时，基础数据必须作为单独、可审查且幂等的数据初始化脚本处理，不能混入结构基线。

## 从独立 WMS 库合并

已有独立 `wms` 库迁入 `ruoyi-vue-pro` 时，必须按以下顺序执行，不能只复制已经加前缀的表后直接启动应用：

1. 停止 ModernWMS 写入并备份目标库；确认目标库不存在与源 WMS 表同名的未加前缀表。
2. 将源 WMS 表结构和数据按经评审的迁移方案导入；不要复制 ERP 表或旧 EF/Flyway 历史表。
3. 核对目标库没有遗留未加前缀的 WMS 表，逐表比较行数、列结构、索引和外键。
4. 如果结构与 V1 完全一致，按 `flyway/README.md` 执行一次性 `-BaselineExisting`；脚本指纹校验不通过时必须修正物理结构，禁止强行补历史。

已经完成前缀迁移的数据库不得重复执行第 2、3 步。源 `wms` 库应保留为迁移回滚副本，确认稳定前不要删除。

## 配置

本机开发示例：

```powershell
dotnet user-secrets set "ConnectionStrings:MySqlConn" "Server=127.0.0.1;Port=3306;Database=ruoyi-vue-pro;User ID=YOUR_USER;Password=YOUR_PASSWORD;Character Set=utf8mb4;" --project backend/ModernWMS
```

部署环境的等价变量名为：

```text
ConnectionStrings__MySqlConn
```

建议分别设置迁移账号和应用账号。迁移账号需要建表、改表和索引权限；稳定运行后的应用账号只保留实际业务所需的数据权限。

## 验收

在一次性空开发库执行迁移后，使用只读 MySQL 客户端核对：

- `wms_flyway_schema_history` 中版本 1 成功；
- V1 创建 50 张 `wms_*` 自有表；
- 没有创建、修改或复制 ERP 表及业务数据；
- 再次执行只读 `info` 和 `validate` 均成功。

## 备份与变更

执行新 Flyway migration 前先备份 `ruoyi-vue-pro`。不要通过删除数据库处理版本升级；新增结构变更应新增不可变的版本化 SQL，并先在一次性本机测试库验证。
