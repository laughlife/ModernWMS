# 现有文档索引

更新时间：2026-08-25

初始化阶段保留原位置，不批量移动。以下按主题给出入口；使用前仍需与当前代码核对。

## 当前接续入口

- `CURRENT_WORK_SUMMARY_2026-08-14.md`：开发环境启动结果、物流注册故障根因、生产补注册验证、当前受限 XXL-JOB 运行方式和下次优先事项。
- `库存预占统一生命周期总体计划.md`：库存预占3.0业务定稿；仅保留赛狐装箱任务和库存调拨两类来源，包含结算公式、提醒、页面、迁移、退役门禁和环境接续说明。

## ModernWMS

- 开发/数据库/部署/基线：`ModernWMS/docs/development.md`、`database.md`、`deployment.md`、`baseline.md`。
- 项目介绍与启动：`ModernWMS/README.zh_CN.md`、`backend/README.md`、`frontend/README.md`。
- 业务与演进：`ModernWMS/发货流程.md`、`升级计划.md`、`design-qa.md`。

## ruoyi-vue-pro

- 采购 ERP：`doc/采购ERP功能说明详单.md`、`赛狐采购日志接入说明.md`、采购交接设计/实施计划。
- 权限体系：`doc/新权限体系*.md`、`权限字段映射清单.md`、用户身份与服务器数据更新方案。
- 物流与 FBA：`doc/物流*.md`、`doc/ziniao/`。
- 合规/企业登记：`doc/compliance_*.md`、`合规信息功能分析.md`、企业登记部署说明。
- 美国商标：`doc/美国商标*.md`、USPTO/字典/回执/历史绑定等专题。
- 基础设施：`doc/阿里云OSS原生存储说明.md`、框架子模块内 Spring Boot 入门文档。

## xxl-job

- 调度基础：`doc/XXL-JOB官方文档.md`、`QUICK_START.md`。
- 项目任务总览：`doc/项目任务清单与调用链总览.md`。
- FBA/订单/物流：`doc/FBA货件信息功能说明详单.md`、`FBA货件详情抓取与货代面单生成文档.md`、`订单追踪完整链路说明.md`。
- 紫鸟：`doc/ziniao/`。
- 重构历史：`doc/REFACTOR_SUMMARY.md`、`TASK_GROUP_REFACTOR_README.md`。

## yudao-ui-admin-vue3

- 采购：`doc/采购发货页面说明.md`，另需联读后端 `ruoyi-vue-pro/doc/采购ERP功能说明详单.md`。
- 企业登记/WebSocket：`doc/企业登记*.md`、`冻结*.md`。
- 合规：`doc/compliance_*.md`、`合规*.md`。
- 物流、权限、商标：`doc/物流管理模块.md`、`用户角色层级与交叉管理说明.md`、`美国商标*.md`。
- 项目入口：`README.md`、`项目介绍.md`。

## 历史资料说明

- `.serena/memories`、`.cursor` 进度文件、`CLAUDE.md` 和旧 Memory 内容只能作为历史线索，不是当前 OMX 工作流的强制入口。
- 若历史文档仍准确，可整理进所属仓库 `doc/`；若与代码冲突，应标记“历史/已失效”而不是直接删除。
