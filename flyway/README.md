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

本脚本不提供生产数据库或远程数据库的绕过开关。生产迁移必须单独设计、评审并授权，不能用本机开发脚本执行。

既有 EF 数据库转为 Flyway 前，需要先完成物理结构审计和一次性基线评审。此目录当前不会自动 baseline，也不会补写任何迁移历史。
