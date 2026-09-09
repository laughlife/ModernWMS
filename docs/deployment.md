# Linux 生产部署

生产发布包面向已经完成基础环境配置的 Linux 服务器，不使用 Docker。发布脚本只在本机生成 ZIP，不会连接服务器、重启服务或执行数据库迁移。

## 1. 一键生成发布包

在 Windows PowerShell 7 中执行，无需参数，也不需要 WSL：

```powershell
& 'D:\ai-dev\ModernWMS\scripts\一键压缩发布包.ps1'
```

脚本会在隔离的临时目录中执行前端 `npm ci` 和生产构建，并发布 `linux-x64`、framework-dependent 的 .NET 10 后端。最终只保留：

```text
artifacts/publish/wms.zip
```

ZIP 一级目录为 `frontend`、`backend`，并包含 `RELEASE_NOTES.txt`。其中：

- `frontend` 可直接替换 `/opt/modernwms/frontend`。
- `backend` 可直接替换 `/opt/modernwms/backend`。
- `RELEASE_NOTES.txt` 记录版本、构建时间、提交信息和数据库迁移清单；包内不包含 Nginx、systemd 或数据库迁移脚本。
- 后端固定监听 `http://127.0.0.1:21011`，与 Nginx 的 `/api/` 代理一致。

压包读取当前工作区代码，在隔离目录安装前端依赖并构建。ZIP 通过目录结构和逐文件 SHA256 校验后才替换旧 `wms.zip`；构建或校验失败会保留旧包。重复运行同仓库压包任务会被拒绝。

压包过程只调用 `dotnet publish`，不会执行 `dotnet watch run`。如果控制台显示 `watch run`，它属于开发启动任务。开发默认监听回环地址；`wwwroot` 缺失警告与前后端分离部署有关，不能据此判定压包失败。

## 2. 发布包的配置和秘密

目标服务器必须安装 .NET 10 ASP.NET Core Runtime 和 Nginx，并提前准备 `wms.nyamtn.com` 的 TLS 证书。

脚本不会读取本机 .NET User Secrets。发布包的 `appsettings.json` 和 `appsettings.Production.json` 中，连接字符串及 `TokenSettings:SigningKey` 会被清空；开发配置不进入发布包。服务器需要通过既有服务配置或安全配置来源提供：

- `ConnectionStrings__MySqlConn`：生产数据库连接字符串。
- `TokenSettings__SigningKey`：生产签名密钥。
- `ASPNETCORE_ENVIRONMENT=Production`：加载生产配置。

替换发布目录时保留服务器外部配置，不要指望 ZIP 携带可直接使用的生产凭据。仓库中的 `appsettings*.json` 不得保存真实密码或签名密钥。

## 3. 数据库结构升级

Web Host 不注册 EF DbContext，也不执行数据库初始化。生产结构升级必须在备份后，通过单独评审和授权的 Flyway 发布流程完成。

仓库的 `scripts/Update-Database.ps1` 仅允许本机开发库，不能用于生产环境。发布脚本不会打包或自动执行数据库迁移，`--initialize-database-only` 也会被应用拒绝。

## 4. 部署后端

先按现有生产变更流程停止后端服务，再替换发布目录。后端服务的工作目录必须是 `/opt/modernwms/backend`，因为 `nlog.config`、`appsettings.json` 等文件按当前工作目录加载。

等价的前台启动命令为：

```bash
cd /opt/modernwms/backend
ASPNETCORE_ENVIRONMENT=Production dotnet ModernWMS.dll
```

生产环境应继续使用现有服务管理器启动，不要把前台命令当作长期守护方案。服务账号应拥有日志目录写权限和 MySQL 访问权限。

## 5. 部署前端和 Nginx

将 `frontend` 发布到 `/opt/modernwms/frontend`。包内不包含 Nginx 配置，沿用服务器已有配置，并核对域名、证书路径和目录。生产变更流程中检查：

```bash
nginx -t
```

服务器配置需要包含单页应用回退、静态资源缓存和 `/api/` 到 `127.0.0.1:21011` 的反向代理。只有在 `nginx -t` 成功后，才按现有生产流程重新加载 Nginx。

## 6. 发布后检查

服务启动后执行：

```bash
curl --fail --show-error http://127.0.0.1:21011/health
```

预期 HTTP 状态为 `200`，响应正文为 `Healthy`。随后检查正式域名、登录、菜单、仓库、货主、SKU、入库、出库、库存、调整和打印功能，并确认日志中没有持续异常。

推荐发布顺序：备份 MySQL、停止后端、完成已评审的 Flyway 升级、替换前后端目录、启动后端、验证健康检查、核验并重新加载 Nginx、完成业务验收。
