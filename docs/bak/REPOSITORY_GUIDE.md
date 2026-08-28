# 仓库指南

更新时间：2026-08-14

## ModernWMS

### 结构

- `backend/ModernWMS`：ASP.NET Core Web Host、配置、初始化和 EF Migration。
- `backend/ModernWMS.Core`：DBContext、JWT、通用服务、中间件、Swagger、多租户和任务基础设施。
- `backend/ModernWMS.WMS`：ASN、出库、库存、仓内作业、仓库、权限、打印和费用等领域实现。
- `backend/ModernWMS.Tests`：xUnit 测试。
- `frontend/src/api`、`frontend/src/view`：WMS 前端 API 与页面。

### 入口与验证

- 后端入口：`backend/ModernWMS/Program.cs`。
- 运行：`dotnet run --project backend/ModernWMS`。
- 仅初始化数据库：`dotnet run --project backend/ModernWMS -- --initialize-database-only`，该操作会改变数据库，执行前必须确认目标。
- 后端验证：从 `backend/ModernWMS.Tests` 的针对性测试开始，再按需 build solution。
- 前端验证：以 `frontend/package.json` 当前脚本为准，包含 unit、build、E2E 入口。

### 风险

- 当前工作树已有大量未提交改动，覆盖后端、前端、迁移和文档；后续必须逐文件保护。
- `.env` 和 appsettings 中存在环境地址/连接配置，文档不得复制敏感值。

## ruoyi-vue-pro

### 结构

- `yudao-server`：Spring Boot 启动与模块聚合入口。
- `yudao-framework`：安全、Web、MyBatis、Redis、MQ、Job、WebSocket 等基础设施。
- `yudao-module-system`、`infra`、`bpm`：系统、基础设施和工作流。
- `yudao-module-erp`、`yudao-module-ruiyi`：ERP 主业务与公司特定集成。
- `sql/mysql`：MySQL 初始化与演进脚本。
- `doc`：采购、权限、物流、FBA、合规、紫鸟、OSS、商标等业务文档。

根 `pom.xml` 当前启用了 dependencies/framework/server/system/infra/bpm/erp/ruiyi/ai 等聚合模块；`yudao-module-wms` 源码目录存在但根聚合未启用。Java 版本配置与分支名存在需要后续核实的差异：根 POM 指向 Java 21，而当前分支名为 `ruiyi-jdk17`。

### 入口与验证

- 启动配置：`yudao-server/src/main/resources/application*.yaml`。
- 数据库初始入口：`sql/mysql/ruoyi-vue-pro.sql`。
- 验证时先读根 POM，只构建/测试受影响模块及其必要依赖；外部调用和数据写入另行确认。

### 风险

- 仓库体量大，功能域和历史文档并存；不能仅凭目录存在判断模块已启用。
- 配置文件可能包含开发或生产连接信息，只可引用键名和结构，不复制值。

## xxl-job

### 结构

- `xxl-job-core`：执行器与调度通信核心。
- `xxl-job-admin`：调度中心、任务/执行器/日志管理。
- `xxl-job-executor`：实际业务执行器和任务 handler。
- `doc`：官方说明及项目定制任务、FBA、订单追踪、财务和紫鸟资料。

关键管理入口包括 `JobInfoController`、`JobGroupController`、`JobLogController`。业务联动必须继续下钻到 executor handler，不能只看 admin 页面。

### 验证

- 以根 `pom.xml` 的模块关系为准，优先针对受影响模块测试。
- 启动 admin/executor、执行外部自动化或写数据库会产生状态，必须先确认环境和授权。

## yudao-ui-admin-vue3

### 结构

- `src/api`：后端 API 契约封装。
- `src/views`：业务页面。
- `src/permission.ts`、`src/store/modules/permission.ts`：动态路由和权限。
- `doc`：前端业务、联调、合规、采购、商标和企业登记文档。

### 入口与验证

- 依赖与脚本：`package.json`。
- 环境：`.env*`，本地通常通过 `/admin-api` 对接 Ruoyi。
- 优先运行针对性测试、`pnpm ts:check`、lint 和相应构建；交互行为再使用浏览器/E2E 证明。

### 采购页面约束

- 新流程目录为 `src/views/erp/purchaseTask`；`warehousepurchase` 仅作历史参考。
- `action/status` 必须按字符串处理；采购动作字典使用字符串字典 helper。
- 权限和数据范围不得由前端自行猜测。
