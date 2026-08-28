# 工程知识库

本目录用于沉淀 `/mnt/d/ai-dev` 四仓协同产品的长期工程认知。它不是一次性分析报告，而是后续开发持续维护的事实入口。

## 阅读顺序

1. [CURRENT_WORK_SUMMARY_2026-08-14.md](CURRENT_WORK_SUMMARY_2026-08-14.md)：当前工作结果、运行状态、风险和下次会话接续步骤。
2. [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md)：产品边界、技术栈和仓库职责。
3. [REPOSITORY_GUIDE.md](REPOSITORY_GUIDE.md)：四个仓库的入口、模块、验证方式和关注点。
4. [CROSS_REPOSITORY_CONTRACTS.md](CROSS_REPOSITORY_CONTRACTS.md)：共享数据库、API、权限、任务与消息边界。
5. [DOCUMENT_INDEX.md](DOCUMENT_INDEX.md)：已有项目文档的主题索引。
6. [DEVELOPMENT_LOG.md](DEVELOPMENT_LOG.md)：功能、缺陷、根因、修复原因、验证与当前状态的记录规范。
7. [INITIALIZATION_2026-08-14.md](INITIALIZATION_2026-08-14.md)：本次初始化范围、证据和待补充项。

## 事实优先级

1. 当前可执行代码、数据库迁移、测试、配置结构和接口模型。
2. 与当前代码一致的项目文档。
3. 本目录的工程级汇总。
4. 历史设计、迁移计划、旧 Memory 或工具缓存。

若文档与实现冲突，应先记录冲突，再以验证后的实现和产品确认结果更新文档，不能静默选择一方。

## 写入规则

- 新理解必须标明证据路径；推断和未确认项要明确标识。
- 单仓细节写入所属仓库的 `doc/` 或 `docs/`；跨仓结论写入本目录。
- 修复记录至少包含：现象、影响、根因、形成原因、修复方案、为何这样修、验证证据、剩余风险。
- API、数据库字段、状态机、权限、任务路由或消息协议变化必须同步更新跨仓契约。
- 不记录密码、Token、私钥、个人数据或生产连接信息。
- OMX 是协作主流程。Agentic Tools MCP 只作为可选辅助，不能替代 Markdown 的长期事实记录。

## 文档移动策略

初始化阶段不移动各仓库既有文档，原因是尚未全面核对代码引用、历史链接和 Git 语义。先在本目录建立索引；确认无引用风险后，再按所属仓库逐批整理。
