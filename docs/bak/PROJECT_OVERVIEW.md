# 工程总览

更新时间：2026-08-14

## 产品组成

| 仓库 | 产品职责 | 主要技术 | 当前关键边界 |
| --- | --- | --- | --- |
| `ruoyi-vue-pro` | ERP 后端与主业务平台 | Spring Boot、Spring Security、MyBatis-Plus、MySQL、Redis、Flowable、Quartz | 采购、财务、人事、权限、ERP/Ruiyi 集成；向前端提供 `/admin-api`；调用或协调 XXL 执行任务；与 WMS 共享数据库事实 |
| `yudao-ui-admin-vue3` | ERP 管理后台 | Vue 3、TypeScript、Vite、Element Plus、Pinia、Vue Router、Axios | 消费 Ruoyi API 和权限结果；不直接访问数据库；Infra 定时任务页面属于 Quartz API |
| `xxl-job` | 调度中心与外部任务执行 | Java、Maven、Spring Boot、MyBatis、MySQL、XXL-JOB | 调度/执行物流、FBA、紫鸟等任务；不能与 Ruoyi 内置 Quartz 混为一谈 |
| `ModernWMS` | 仓储管理系统 | ASP.NET Core/.NET 10、EF Core 10、MySQL；Vue 3、TypeScript、Vite、Vuetify | 收货、出库、库存与仓内作业；通过共享库和映射表使用 ERP 主数据 |

证据入口：`ruoyi-vue-pro/pom.xml`、`ruoyi-vue-pro/yudao-server/`、`yudao-ui-admin-vue3/package.json`、`xxl-job/pom.xml`、`ModernWMS/global.json`、`ModernWMS/backend/ModernWMS.sln`、`ModernWMS/frontend/package.json`。

## 核心业务链路初识

### ERP 管理链路

`yudao-ui-admin-vue3` 页面与 API 封装 -> `/admin-api/*` -> `ruoyi-vue-pro` Controller/Service/Mapper -> MySQL、Redis 或外部服务。

权限与数据范围以 Ruoyi 后端为安全边界；前端路由和按钮仅负责展示控制。前端入口证据为 `src/permission.ts` 和 `src/store/modules/permission.ts`。

### ERP 与 XXL-JOB

目前存在两套任务概念：

- Ruoyi Infra/Quartz：前端 `src/api/infra/job/index.ts` 调用 `/admin-api/infra/job/*`。
- XXL-JOB：独立仓库包含 admin/core/executor，并由 Ruoyi 在部分物流、FBA、Ruiyi、紫鸟流程中通过 HTTP、Redis、WebSocket 或共享数据库协作。

直接证据包括：

- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticstrack/integration/XxlLogisticsTrackRegisterClient.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/controller/admin/ziniao/webdriver/ZiniaoWebDriverInternalController.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/enums/ziniao/ZiniaoWebDriverConstants.java`
- `xxl-job/xxl-job-admin/src/main/java/com/xxl/job/admin/controller/biz/JobInfoController.java`

### ERP 与 ModernWMS

ModernWMS 与 ERP 使用同一 MySQL 数据库。WMS 自有表采用 `wms_` 前缀，并通过 ERP 实体映射、仓库 ID、商品/供应商映射和 `wms_erp_*` 表连接 ERP 事实。

直接证据包括 `ModernWMS/backend/ModernWMS.Core/DBContext/SqlDBContext.cs`、`ErpCommodityEntity.cs`、`ModernWMS/backend/ModernWMS/Migrations/`。

## 当前阶段判断

- **已验证事实**：四仓职责、主要技术栈、入口、已有文档目录、Ruoyi Quartz 与 XXL-JOB 是不同任务体系、WMS 与 ERP 的共享库关系。
- **较高置信推断**：当前产品采用“ERP 维护业务主状态，XXL 执行外部任务，WMS 维护仓内状态”的分工，但每条业务流仍需按代码逐项确认。
- **尚未完成**：所有业务模块、表级数据归属、任务 handler 全清单、Redis/WebSocket 协议全清单、权限点全清单、完整部署拓扑和生产运行参数。
