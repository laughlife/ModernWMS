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
