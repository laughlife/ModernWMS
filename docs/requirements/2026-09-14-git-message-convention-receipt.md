# ModernWMS 提交历史整理回执

- 原需求：`docs/requirements/2026-09-14-git-message-convention.md`
- 状态：已完成（仅 ModernWMS）。
- 仓库与分支：`D:\ai-dev\ModernWMS` / `ruiyi`。
- 历史范围：从原提交 `d452340bae44f00dbdeec87b067f95e185cc743c` 的父提交到原分支头，共 21 个提交对象；其中标题含 `REQ` 的 16 个提交已改名。
- 标题映射：业务修复为 `fix(模块): ...`，文档记录为 `docs(模块): ...`，保留原中文主题并移除标题中的 `REQ` 编号包装；未改变提交树、作者、时间和父子顺序。
- 当前头：`027fdcb1732c4f8b0f49b0e30ac6dda1d5c26ea1`。
- 回滚引用：`backup/commit-messages-before-20260914` 指向改名前头 `e0568934e080dd62781bc1508d56a1ce5d48a514`。
- 规则变更：`AGENTS.md` 新增 Conventional Commits 前缀约定，需求编号改放正文或执行回执，不再强制放在标题。
- 静态证据：`git log` 检查当前分支标题含 `REQ` 数为 0；改名前后分支顶端内容差异为空（本回执文件除外）；Git 工作区原有删除、修改和未跟踪文件均保留，未纳入本次提交。
- 未验证项：未启动项目、未连接数据库、未运行测试或构建；远端分支未修改，未执行 `git push`。
