# MySQL 数据库与初始化

## 目标环境

- MySQL 8.4
- 地址：`127.0.0.1:3306`
- 数据库：`wms`
- 字符集：`utf8mb4`

仓库不保存真实数据库密码。开发机使用 .NET User Secrets，部署环境使用受保护的环境变量或密钥管理服务。

## 初始化方式

应用默认在启动时执行 `DatabaseInitializer.InitializeAsync`：先运行 EF Core Migration，再在事务中补齐嵌入程序集的基础数据。也可以只初始化并退出：

```powershell
dotnet run --project backend/ModernWMS -- --initialize-database-only
```

当前初始化基线应包含：

| 数据 | 数量 |
| --- | ---: |
| 菜单 | 19 |
| 角色菜单关系 | 19 |
| 管理员用户 | 1 |
| 管理员角色 | 1 |

当前初始 Migration 创建 35 张业务表；加上 `__EFMigrationsHistory` 后，`wms` 中共有 36 张表。

初始化逻辑按基础数据固定主键查询，仅添加缺失记录，因此重复执行不会重复插入。它不会覆盖同一主键上已经存在的业务修改。

## 配置

本机开发示例：

```powershell
dotnet user-secrets set "ConnectionStrings:MySqlConn" "Server=127.0.0.1;Port=3306;Database=wms;User ID=YOUR_USER;Password=YOUR_PASSWORD;Character Set=utf8mb4;" --project backend/ModernWMS
```

部署环境的等价变量名为：

```text
ConnectionStrings__MySqlConn
```

建议分别设置迁移账号和应用账号。迁移账号需要建表、改表和索引权限；稳定运行后的应用账号只保留实际业务所需的数据权限。

## 验收

连续执行两次初始化命令，然后使用有只读权限的 MySQL 客户端核对：

- `__EFMigrationsHistory` 中存在当前 Migration；
- 业务表已经创建；
- 上表所列基础数据数量保持不变；
- 管理员可以登录并取得菜单。

基础账号为 `admin` / `1`，只用于初始化后的首次登录。非一次性测试环境必须立即修改密码。

## 备份与变更

执行新 Migration 前先备份 `wms`。不要通过删除数据库来处理版本升级；新增结构变更应生成并审查新的 EF Core Migration，再在测试库验证回滚和数据兼容性。
