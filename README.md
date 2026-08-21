# ModernWMS 仓库管理系统

ModernWMS 是一个前后端分离的仓库管理系统。本分支已完成整体技术栈升级，作为独立的 Windows 原生项目继续开发，不使用 Docker。

## 当前技术栈

- .NET SDK 10.0.302、ASP.NET Core 10、Dapper、MySqlConnector、Flyway
- 后端目标框架固定为 `net10.0`
- MySQL 8.4
- Node.js 24.16.0 或更高版本、npm 11.17.0
- Vue 3.5、TypeScript 6、Vite 8、Vuetify 4
- VXE Table 4、ECharts 6、Pinia 4、Vue Router 5

## 环境要求

- Windows 10/11 或仍受支持的 Windows Server
- .NET SDK 10.0.302；仓库根目录的 `global.json` 会固定 SDK 功能带
- 全部后端项目都以 `net10.0` 为目标框架；后端开发、构建和运行均使用 .NET 10 系列 SDK/Runtime
- Node.js 24.16.0 或更高版本
- 本机 `127.0.0.1:3306` 可访问的 MySQL 8.4，数据库名为 `ruoyi-vue-pro`

## 快速启动

先用 .NET User Secrets 配置数据库连接和 JWT 签名密钥。真实密码不得写入或提交到仓库配置文件。

```powershell
dotnet user-secrets set "ConnectionStrings:MySqlConn" "Server=127.0.0.1;Port=3306;Database=ruoyi-vue-pro;User ID=你的账号;Password=你的密码;Character Set=utf8mb4;" --project backend/ModernWMS
dotnet user-secrets set "TokenSettings:SigningKey" "请替换为至少32个UTF-8字节的随机密钥" --project backend/ModernWMS
dotnet run --project backend/ModernWMS
```

后端默认地址为 `http://localhost:21011`，Swagger 位于应用根路径，健康检查地址为 `/health`。

再打开一个 PowerShell 窗口启动前端：

```powershell
cd frontend
npm ci
npm run dev
```

浏览器访问 `http://127.0.0.1:80`。账号由开发或部署环境显式创建，仓库不再内置默认管理员密码。

## 数据库初始化

ModernWMS 与 Ruoyi/ERP 共用 `ruoyi-vue-pro` 数据库，WMS 自有表统一使用 `wms_` 前缀。应用启动和代码热重载都不会修改数据库；结构变更只能通过显式 Flyway 命令执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 -ConfirmDevelopmentDatabase
powershell -ExecutionPolicy Bypass -File scripts\Update-Database.ps1 -ConfirmDevelopmentDatabase -Apply
```

脚本只允许本机回环地址上的 `ruoyi-vue-pro` 开发库；默认仅执行 `info` 和 `validate`，传入 `-Apply` 才会修改结构。详情见 [数据库说明](docs/database.md)。

## 验证命令

```powershell
dotnet restore backend/ModernWMS.sln
dotnet build backend/ModernWMS.sln --configuration Release --no-restore
dotnet test backend/ModernWMS.sln --configuration Release --no-build

cd frontend
npm ci
npm run test:unit
npm run build
npm run test:e2e
```

## 项目文档

- [本机开发](docs/development.md)
- [数据库与初始化](docs/database.md)
- [Windows 原生部署](docs/deployment.md)
- [升级前行为基线](docs/baseline.md)
- [历史升级计划（仅供追溯）](升级计划.md)
