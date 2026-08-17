# Windows 原生部署

本项目按 Windows 原生进程部署，不使用 Docker。

## 1. 生成发布产物

在仓库根目录执行：

```powershell
dotnet publish backend/ModernWMS/ModernWMS.csproj --configuration Release --output artifacts/backend

cd frontend
npm ci
npm run build
cd ..
```

后端产物位于 `artifacts/backend`，前端静态文件位于 `frontend/dist`。

## 2. 配置运行环境

部署机安装 .NET 10 Runtime，或在后续发布策略中改为 self-contained。MySQL 8.4 需已创建 `ruoyi-vue-pro` 数据库并允许应用账号访问；WMS 表使用 `wms_` 前缀。

不要把密码写入仓库文件。可为运行后端的 Windows 服务账号设置受保护的环境变量：

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:21011
ConnectionStrings__MySqlConn=Server=127.0.0.1;Port=3306;Database=ruoyi-vue-pro;User ID=...;Password=...;Character Set=utf8mb4;
TokenSettings__SigningKey=至少32个UTF-8字节的随机密钥
```

生产前端构建默认请求 `http://127.0.0.1:21011`。如果浏览器从其他机器访问，应在构建前调整 `frontend/.env.production`，或由 IIS/反向代理提供同源 API 路径。

如果前后端不同源，还必须在生产配置中通过 `Cors:AllowedOrigins` 明确列出前端来源；不要使用任意来源通配策略。

## 3. 更新结构并启动后端

Web Host 不注册 EF DbContext，也不执行数据库初始化。发布前应先通过单独评审和授权的 Flyway 发布流程完成结构升级；本仓库的 `scripts/Update-Database.ps1` 仅允许本机开发库，不能用于生产环境。

启动发布产物时，工作目录必须是发布目录；`nlog.config`、`appsettings.json` 等文件按当前工作目录加载：

```powershell
cd artifacts/backend
dotnet ModernWMS.dll
```

建议将后端注册为 Windows 服务，并把工作目录设置为 `artifacts/backend` 的实际部署路径。服务账号应拥有日志目录写权限和 MySQL 访问权限。

## 4. 托管前端

将 `frontend/dist` 发布到 IIS 静态网站或组织现有的 Windows Web 服务器。单页应用需要把不存在的前端路由回退到 `index.html`，同时不要把 API 请求回退到前端页面。

开发或临时验收可以使用：

```powershell
cd frontend
npm run preview -- --host 127.0.0.1
```

`vite preview` 只用于验收构建产物，不作为长期生产 Web 服务器。

## 5. 发布后检查

```powershell
Invoke-WebRequest http://127.0.0.1:21011/health -UseBasicParsing
```

预期 HTTP 状态为 `200`，响应正文为 `Healthy`。随后检查 Swagger、登录、菜单、仓库、货主、SKU、入库、出库、库存、调整和打印功能，并确认日志中没有持续异常。

发布新版本前备份 MySQL。升级时先停止后端服务，通过独立 Flyway 发布流程应用已评审的版本化 SQL，再替换发布目录、启动服务并完成健康检查。`--initialize-database-only` 已被禁用，传入该参数会直接拒绝启动。
