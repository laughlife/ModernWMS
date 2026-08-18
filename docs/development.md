# 本机开发指南

本文档适用于 Windows 原生开发环境；项目不使用 Docker。

## 1. 安装并检查工具

```powershell
dotnet --version
node --version
npm --version
```

目标版本为 .NET SDK `10.0.302`、Node.js `24.16.0` 或更高版本、npm `11.17.0`。后端目标框架固定为 `net10.0`，开发、构建和运行均使用 .NET 10 系列 SDK/Runtime。`global.json` 会让 .NET CLI 选择 10.0.302 功能带，`frontend/package.json` 定义了 Node 和 npm 要求。

MySQL 8.4 应监听 `127.0.0.1:3306`，数据库名为 `ruoyi-vue-pro`。ModernWMS 与 Ruoyi/ERP 共用该数据库，WMS 自有表使用 `wms_` 前缀。建议给应用使用专用的最小权限账号，不要长期使用 `root`。

## 2. 配置后端

真实连接串和 JWT 密钥使用 User Secrets 保存，不写入 `appsettings.json`：

```powershell
dotnet user-secrets set "ConnectionStrings:MySqlConn" "Server=127.0.0.1;Port=3306;Database=ruoyi-vue-pro;User ID=YOUR_USER;Password=YOUR_PASSWORD;Character Set=utf8mb4;" --project backend/ModernWMS
dotnet user-secrets set "TokenSettings:SigningKey" "REPLACE_WITH_AT_LEAST_32_UTF8_BYTES" --project backend/ModernWMS
dotnet user-secrets list --project backend/ModernWMS
```

JWT 签名密钥必须至少包含 32 个 UTF-8 字节。共享或部署环境应改用受保护的进程环境变量或密钥管理服务，变量名分别为 `ConnectionStrings__MySqlConn` 和 `TokenSettings__SigningKey`。

## 3. 推荐：统一启动和停止

日常开发从仓库根目录运行统一启动器：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-Development.ps1
```

启动器会依次完成以下操作：

1. 检查 `dotnet`、`npm`、前端依赖和 21011/80 端口；端口被占用时会报告 PID，但不会终止未知进程。
2. 普通启动不会检查或修改数据库；数据库迁移使用下文独立的 Flyway 命令。
3. 启动后端变更检测进程 `scripts\Watch-Backend.ps1`：每 60 秒检测一次后端源码变更；检测到变更后，等待源码连续 60 秒无新变化、且距上次自动重启满 60 秒，才重新编译并重启后端。这样可以避免 AI 批量修改多个文件时后端反复重启、甚至上一次重启还没完成就再次触发。检测与重启的事件都会写入日志目录下的 `backend.watcher.log`，后端运行日志仍为 `backend.stdout.log` / `backend.stderr.log`。
4. 后端健康检查通过后，由 `Watch-Backend.ps1` 启动前端 Vite（端口 80）并把日志保存到输出中显示的临时目录；变更检测进程会持续确保前后端都在运行（后端变更自动重启、前端掉线自动拉起）。统一启动器会给前端进程覆盖本机 API 地址为 `http://127.0.0.1:21011`；手工执行 `npm run dev` 时仍使用前端环境文件中的地址。

数据库迁移与常驻进程完全分离，不会影响代码自动更新：后端由 `scripts\Watch-Backend.ps1` 每分钟检测源码变更，在源码稳定满 60 秒且距上次重启满 60 秒后才自动重启（限制重启频率）；前端仍由 Vite 开发服务器提供文件监听和 HMR。需要立即应用最新后端代码时，运行 `scripts\Stop-Development.ps1` 后重新执行启动器即可。

只做环境和端口检查，不初始化数据库或启动进程：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-Development.ps1 -CheckOnly
```

停止本启动器启动的前后端：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Stop-Development.ps1
```

停止脚本只会按状态文件中的 PID 和进程启动时间双重验证后，分别终止控制进程和实际监听进程，不会按进程名批量杀进程。不要同时使用 Rider 组合启动和统一启动器；如果端口已被 Rider 或其他程序占用，先在对应工具中正常停止。

如果已用其他方式（如 Rider 或独立终端）运行 Vite，可以直接运行后端变更检测脚本，它会自动发现 `backend\ModernWMS\ModernWMS.csproj` 并复用统一的临时状态目录；如果对应端口没有前端在监听，脚本会顺带把前端一起拉起来：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Watch-Backend.ps1
```

可选参数：`-Project <csproj 路径>`、`-Port <后端端口>`（默认 21011）、`-FrontendPort <前端端口>`（默认 80）、`-IntervalSeconds <秒>`（默认 60，最小 10）。脚本会先检查端口占用和后端控制进程冲突，确认无误后启动后端并进入每分钟检测循环；检测循环里同时确保前端 Vite 运行（端口空闲则自动启动），后端源码稳定后自动重启后端。按 `Ctrl+C` 退出时会自动清理后端与前端进程。停止该脚本启动的进程同样使用 `scripts\Stop-Development.ps1`。

## 4. 显式检查或更新数据库

```powershell
$env:FLYWAY_URL = 'jdbc:mysql://127.0.0.1:3306/ruoyi-vue-pro'
$env:FLYWAY_USER = 'YOUR_MIGRATION_USER'
$env:FLYWAY_PASSWORD = 'YOUR_MIGRATION_PASSWORD'

# 默认只执行 info 和 validate，不修改结构
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 -ConfirmDevelopmentDatabase

# 备份并确认目标环境后，才显式应用迁移
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 -ConfirmDevelopmentDatabase -Apply

# 仅供已有 WMS 表、尚无 Flyway 历史的本机开发库使用一次
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 `
  -ConfirmDevelopmentDatabase -BaselineExisting `
  -ConfirmExistingSchemaFingerprint 'WMS_SCHEMA_MATCHES_V1'
```

脚本只接受固定的 Flyway `11.15.0`，并强制使用 `wms_flyway_schema_history`、`cleanDisabled=true` 和 `baselineOnMigrate=false`。它仅允许回环地址、且库名严格等于 `ruoyi-vue-pro` 的本机开发库，并要求 `-ConfirmDevelopmentDatabase`；生产库和远程库没有绕过开关。已有库的 `-BaselineExisting` 还会对 50 张 WMS 表逐表执行只读结构指纹核验，完全匹配 V1 后才登记基线。工具安装及完整限制见 `flyway/README.md`。连接信息只通过当前进程环境变量或脚本参数传入，不写入仓库；密码只读取 `FLYWAY_PASSWORD`。普通后端启动和文件变更后的自动重启都不会调用 Flyway。

## 5. 手工启动后端

```powershell
dotnet run --project backend/ModernWMS
```

后端默认监听 `http://localhost:21011`：

- Swagger：`http://localhost:21011/`
- 健康检查：`http://localhost:21011/health`

开发环境仅允许来自 `http://localhost`、`http://127.0.0.1`、`http://localhost:80` 和 `http://127.0.0.1:80` 的跨域请求。

## 6. 手工安装并启动前端

```powershell
cd frontend
npm ci
npm run dev
```

浏览器访问 `http://127.0.0.1:80`。开发配置会把 API 请求发送到 `http://127.0.0.1:21011`。

当前仓库路径包含 `#`。如果 npm、Vite 或浏览器测试在该路径下解析异常，可在临时盘符中运行前端命令：

```powershell
subst W: "D:\workspace\c#\ModernWMS"
cd W:\frontend
npm run build
subst W: /d
```

## 7. Rider 一键启动

使用 Rider 打开 `backend/ModernWMS.sln` 后，顶部启动栏会显示仓库共享的两个启动配置：

- `后端：ModernWMS API`：只启动后端 API。
- `前端：Vite`：在 `frontend` 目录执行 `npm run dev`。

前端依赖仍需事先安装；启动配置不会自动执行 `npm ci`。需要稳定地同时启动前后端时，使用本页第 3 节的统一启动器。

## 8. 测试

```powershell
dotnet restore backend/ModernWMS.sln
dotnet build backend/ModernWMS.sln --configuration Release --no-restore
dotnet test backend/ModernWMS.sln --configuration Release --no-build

cd frontend
npm run test:unit
npm run build
npm run test:e2e
```

本机 Playwright 默认使用已安装的 Chrome；CI 环境使用 Playwright 默认浏览器。

## 9. 外部商品图片访问规范

ERP 商品快照中的 `mainImage` 可能指向启用了 Referer 防盗链的腾讯 COS。此类对象在不带 Referer 时可以直接访问，但浏览器从 ModernWMS 页面加载时会因携带站点 Referer 收到 `403 Forbidden`；这不是普通的图片 CORS 问题，也不表示对象一定是私有读。

前端展示 ERP 或外部来源的商品图片时，统一复用 `frontend/src/components/system/product-image.vue`。该组件为实际 `<img>` 设置 `referrerpolicy="no-referrer"`，保留对象存储/CDN 的浏览器直连和缓存能力，并提供空地址及加载失败占位，适合商品列表中的批量图片展示。不要为了普通 `<img>` 展示额外设置 `crossorigin="anonymous"`，否则会把无需 CORS 校验的图片请求升级为必须通过 CORS 校验。

如果后续确认某个对象在无 Referer 请求下仍返回 `401/403`，则应将其视为真正的私有对象，由后端根据对应存储配置生成短期签名 URL；不要把访问密钥放到前端，也不要让 WMS 后端长期代理全部图片流量。
