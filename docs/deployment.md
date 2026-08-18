# Linux 生产部署

生产发布包面向已经完成基础环境配置的 Linux 服务器，不使用 Docker。发布脚本只在本机生成 ZIP，不会连接服务器、重启服务或执行数据库迁移。

## 1. 一键生成发布包

在仓库根目录执行：

```powershell
& '.\scripts\一键压缩发布包.ps1'
```

脚本会在隔离的临时目录中执行前端 `npm ci` 和生产构建，并发布 `linux-x64`、framework-dependent 的 .NET 10 后端。最终只保留：

```text
artifacts/publish/wms.zip
```

ZIP 一级目录为 `frontend`、`backend`、`deploy`，并包含 `部署说明.txt`。其中：

- `frontend` 可直接替换 `/opt/modernwms/frontend`。
- `backend` 可直接替换 `/opt/modernwms/backend`。
- `deploy/nginx/conf.d/wsm.nyamtn.conf` 是生产 Nginx 配置参考。
- 后端固定监听 `http://127.0.0.1:21011`，与 Nginx 的 `/api/` 代理一致。

## 2. 发布包的配置和秘密

目标服务器必须安装 .NET 10 ASP.NET Core Runtime 和 Nginx，并提前准备 `wms.nyamtn.com` 的 TLS 证书。

脚本从本机 .NET User Secrets 读取 `ConnectionStrings:MySqlConn` 和 `TokenSettings:SigningKey`，把数据库主机、端口替换为脚本定义的生产值后写入发布包的 `backend/appsettings.Production.json`。因此 `wms.zip` 含生产可用的敏感配置：

- 不得提交 `wms.zip`、解压后的生产配置或任何秘密到 Git。
- 传输和保存发布包时必须使用受控权限。
- 不得在控制台、日志、工单或聊天中输出连接字符串和签名密钥。

仓库中的 `appsettings*.json` 仍不得保存真实密码或签名密钥。

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

将 `frontend` 发布到 `/opt/modernwms/frontend`。把包内 Nginx 配置复制到服务器前，必须核对域名、证书路径和目录，并运行：

```bash
nginx -t
```

配置已包含单页应用回退、静态资源缓存和 `/api/` 到 `127.0.0.1:21011` 的反向代理。只有在 `nginx -t` 成功后，才按现有生产流程重新加载 Nginx。

## 6. 发布后检查

服务启动后执行：

```bash
curl --fail --show-error http://127.0.0.1:21011/health
```

预期 HTTP 状态为 `200`，响应正文为 `Healthy`。随后检查正式域名、登录、菜单、仓库、货主、SKU、入库、出库、库存、调整和打印功能，并确认日志中没有持续异常。

推荐发布顺序：备份 MySQL、停止后端、完成已评审的 Flyway 升级、替换前后端目录、启动后端、验证健康检查、核验并重新加载 Nginx、完成业务验收。
