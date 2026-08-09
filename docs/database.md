# MySQL 数据库与初始化

## 目标环境

- MySQL 8.4
- 地址：`127.0.0.1:3306`
- 数据库：`ruoyi-vue-pro`（与 Ruoyi/ERP 共用）
- 字符集：`utf8mb4`

ModernWMS 只维护一个数据库连接。WMS 自有表统一使用 `wms_` 前缀，Ruoyi/ERP 原有表保持原名；两个 EF Core Context 仅用于区分模型职责，实际连接到同一个数据库。

仓库不保存真实数据库密码。开发机使用 .NET User Secrets，部署环境使用受保护的环境变量或密钥管理服务。

## 初始化方式

应用默认在启动时执行 `DatabaseInitializer.InitializeAsync`：先运行 EF Core Migration，再在事务中补齐嵌入程序集的基础数据。也可以只初始化并退出：

```powershell
dotnet run --project backend/ModernWMS -- --initialize-database-only
```

当前初始化基线应包含：

| 数据 | 数量 |
| --- | ---: |
| 菜单 | 18 |
| 角色菜单关系 | 18 |
| 管理员用户 | 1 |
| 管理员角色 | 1 |

当前 WMS 业务表统一使用 `wms_` 前缀，迁移历史表为 `wms_ef_migrations_history`。

初始化逻辑按基础数据固定主键查询，仅添加缺失记录，因此重复执行不会重复插入。它不会覆盖同一主键上已经存在的业务修改。

## 从独立 WMS 库合并

已有独立 `wms` 库迁入 `ruoyi-vue-pro` 时，必须按以下顺序执行，不能只复制已经加前缀的表后直接启动应用：

1. 停止 ModernWMS 写入并备份目标库；确认目标库不存在与源 WMS 表同名的未加前缀表。
2. 将源 `wms` 的表结构和数据原样导入 `ruoyi-vue-pro`，包括原 `__efmigrationshistory`。
3. 将目标库中的 `__efmigrationshistory` 重命名为 `wms_ef_migrations_history`。
4. 执行 `dotnet run --project backend/ModernWMS -- --initialize-database-only`。EF 会应用 `UnifyDatabaseWithWmsPrefix`，把 WMS 业务表统一改为 `wms_` 前缀，并确保仓库小组绑定表存在。
5. 核对目标库没有遗留未加前缀的 WMS 表，逐表比较行数、列结构和外键后再开放写入。

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

连续执行两次初始化命令，然后使用有只读权限的 MySQL 客户端核对：

- `wms_ef_migrations_history` 中存在当前 Migration；
- WMS 业务表均以 `wms_` 开头；
- 上表所列基础数据数量保持不变；
- 管理员可以登录并取得菜单。

基础账号为 `admin` / `1`，只用于初始化后的首次登录。非一次性测试环境必须立即修改密码。

## 备份与变更

执行新 Migration 前先备份 `ruoyi-vue-pro`。不要通过删除数据库来处理版本升级；新增结构变更应生成并审查新的 EF Core Migration，再在测试库验证回滚和数据兼容性。
