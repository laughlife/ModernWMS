# 2026-08-14 工程初始化记录

## 本次完成范围

- 只纳入 `ModernWMS`、`ruoyi-vue-pro`、`xxl-job`、`yudao-ui-admin-vue3`。
- 未检查、未修改 `FBAShipmentSync` 和 `workspace`。
- 读取四仓入口、模块配置、主要技术栈、文档目录、关键跨仓代码证据和项目级 `AGENTS.md`。
- 建立顶层知识库和后续修复/状态记录规范。
- 清理旧 `agentic_tools` 强制 Memory 总线、固定 `D:\ai-dev\workspace`、旧 Auto Subagents/v4 worker、默认自动提交和禁止常规验证等过时流程。
- 保留并重新表达共享数据库、采购字符串状态、后端权限、WMS 表前缀、Java 职责归口、紫鸟 WebDriver 安全等有效约束。

## Agentic Tools MCP

- npm 包：`@pimzino/agentic-tools-mcp`。
- 验证版本：`1.8.1`。
- Codex 项目级配置：`.codex/config.toml`，名称 `agentic-tools`，STDIO 命令 `npx -y @pimzino/agentic-tools-mcp@1.8.1`。
- 验证结果：`codex mcp list` 显示 enabled，服务启动输出成功。
- 存储策略：默认 project-specific 模式；本次仅完成安装与启动验证，没有创建 `.agentic-tools-mcp` 项目数据。
- 使用策略：可选辅助，不作为强制消息总线；长期事实写入 Markdown。

Codex 官方 MCP 配置说明见 <https://learn.chatgpt.com/docs/extend/mcp?surface=cli>；上游项目见 <https://github.com/Pimzino/agentic-tools-mcp>。

## Git 与现有改动

- `ModernWMS` 当前分支为 `ruiyi`，初始化前已存在大量未提交改动，覆盖 AGENTS、后端、前端、迁移和文档。
- `ruoyi-vue-pro` 当前分支为 `ruiyi-jdk17`。
- `xxl-job`、`yudao-ui-admin-vue3` 的完整 status 在当前挂载上扫描不稳定/超时，不能据此声称干净。
- 本次不提交、不暂存、不还原任何仓库；后续继续按仓库独立检查和提交。

## 初始未知项

1. Ruoyi 根 POM Java 21 与分支名 `ruiyi-jdk17` 的真实运行基线。
2. ERP 与 WMS 的完整表级读写所有权和发布顺序。
3. ERP 与 XXL 的所有 handler、HTTP、Redis、WebSocket、共享库写回协议。
4. 端到端采购、FBA、物流、收货、出库状态机和补偿策略。
5. 两套权限体系是否存在统一身份或单点登录层。
6. 四仓部署拓扑、环境差异和最小可重复本地启动方案。

## 后续更新方式

后续每次开发先读取与任务相关的顶层文档和仓库文档；新证据直接更新对应主题。避免把未经验证的推断写成“当前事实”，并把缺陷根因、修复选择和验证结果同步写入开发记录。
