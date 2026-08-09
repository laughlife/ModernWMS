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

## 3. 初始化数据库并启动后端

```powershell
dotnet run --project backend/ModernWMS -- --initialize-database-only
dotnet run --project backend/ModernWMS
```

后端默认监听 `http://localhost:21011`：

- Swagger：`http://localhost:21011/`
- 健康检查：`http://localhost:21011/health`

开发环境仅允许来自 `http://localhost`、`http://127.0.0.1`、`http://localhost:80` 和 `http://127.0.0.1:80` 的跨域请求。

## 4. 安装并启动前端

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

## 5. Rider 一键启动

使用 Rider 打开 `backend/ModernWMS.sln` 后，顶部启动栏会显示仓库共享的三个启动配置：

- `后端：ModernWMS API`：只启动后端 API。
- `前端：Vite`：在 `frontend` 目录执行 `npm run dev`。
- `一键启动：前端 + 后端`：同时启动前端和后端，是日常开发推荐入口。

首次使用时在顶部启动配置下拉框选择 `一键启动：前端 + 后端`，以后直接点击右侧运行按钮即可。前端依赖仍需事先安装；启动配置不会自动执行 `npm ci`。

## 6. 测试

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

## 7. 外部商品图片访问规范

ERP 商品快照中的 `mainImage` 可能指向启用了 Referer 防盗链的腾讯 COS。此类对象在不带 Referer 时可以直接访问，但浏览器从 ModernWMS 页面加载时会因携带站点 Referer 收到 `403 Forbidden`；这不是普通的图片 CORS 问题，也不表示对象一定是私有读。

前端展示 ERP 或外部来源的商品图片时，统一复用 `frontend/src/components/system/product-image.vue`。该组件为实际 `<img>` 设置 `referrerpolicy="no-referrer"`，保留对象存储/CDN 的浏览器直连和缓存能力，并提供空地址及加载失败占位，适合商品列表中的批量图片展示。不要为了普通 `<img>` 展示额外设置 `crossorigin="anonymous"`，否则会把无需 CORS 校验的图片请求升级为必须通过 CORS 校验。

如果后续确认某个对象在无 Referer 请求下仍返回 `401/403`，则应将其视为真正的私有对象，由后端根据对应存储配置生成短期签名 URL；不要把访问密钥放到前端，也不要让 WMS 后端长期代理全部图片流量。
