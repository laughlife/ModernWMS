# ModernWMS 原版升级基线

## 仓库状态

- 上游基线提交：`1837e17 Update README.md`
- 上游分支：`master`
- 独立开发分支：`ruiyi`
- 原后端目标框架：`net7.0`
- 升级环境 SDK：`.NET SDK 10.0.302`
- 升级环境 Runtime：`Microsoft.AspNetCore.App 10.0.10`
- Node.js：`24.16.0`
- npm：`11.17.0`

## 原版技术栈

- 后端：ASP.NET Core 7、EF Core 7.0.1、Pomelo MySQL Provider 7.0.0-silver.1。
- 前端：Vue 3.2.45、Vite 4.0.0、TypeScript 4.9.3、Vuetify 3.3.12、VXE Table 4.3.7。
- 数据库配置：默认 MySQL，但仓库交付的参考数据为 SQLite `wms.db`。
- 定时任务：Hangfire 1.7 + MemoryStorage。

## 原版端口差异

- `backend/ModernWMS/Program.cs`：`5555`。
- `backend/ModernWMS/Properties/launchSettings.json`：`5056`。
- `frontend/.env.development`：`21011`。
- `frontend/.env.production`：`20011`。
- `docker/run.sh`：`21011`。
- 升级目标：后端统一为 `21011`，前端开发端口统一为 `5173`。

## SQLite 参考数据

原始 `backend/ModernWMS/wms.db` 保持不变，仅作为 MySQL 初始化数据来源。

| 表 | 行数 |
| --- | ---: |
| `menu` | 19 |
| `rolemenu` | 19 |
| `user` | 1 |
| `userrole` | 1 |
| 其他业务表 | 0 |

原库 Migration 历史：`20230106043721_InitialCreate`，EF Core 版本 `7.0.1`。

## 必须保持的业务入口

1. 登录与刷新 Token。
2. 当前用户菜单与权限。
3. 用户、角色和菜单配置。
4. 仓库、库区和库位。
5. 货主、供应商和客户。
6. 商品分类、SPU 和 SKU。
7. ASN 入库、分拣和上架。
8. 出库单、拣货和发运。
9. 库存查询、库龄、移动、冻结和调整。
10. 盘点与库内加工。
11. 打印模板、打印预览、条码和二维码。
12. Swagger/OpenAPI。
13. Hangfire Dashboard 与周期任务。

## 升级验收原则

- 相同输入应保持相同业务结果；必须改变的行为要有测试和升级说明。
- 编译成功不等于验收完成，关键入口必须经过 API 或浏览器冒烟验证。
- 每次依赖大版本升级独立提交，Vuetify 与 VXE Table 不在同一提交中升级。
- 数据库初始化必须幂等，重复启动不得重复写入基础数据。
