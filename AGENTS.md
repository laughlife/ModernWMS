# ModernWMS repository guidance

This file extends `/mnt/d/ai-dev/AGENTS.md`. The workspace-level rules and `doc/` knowledge base are authoritative for cross-repository work.

## Scope and architecture

- This repository owns the warehouse management application: ASP.NET Core backend, EF Core migrations, WMS domain services, and its Vue frontend.
- Backend dependency direction is `ModernWMS` web host -> `ModernWMS.WMS` domain -> `ModernWMS.Core` infrastructure.
- Core domains include ASN/receipt, dispatch/outbound, stock, adjustment/freeze/move/process/taking, warehouse/location, owner/SKU, printing, freight, user/role/menu, and audit logs.
- Do not inspect or modify the standalone sibling project `FBAShipmentSync` unless explicitly requested.

## Shared database and ERP boundary

- ModernWMS shares the ERP MySQL database with `ruoyi-vue-pro`. Existing ERP tables remain ERP-owned; WMS code may map the explicitly required ERP entities and columns.
- New WMS-owned tables must use the `wms_` prefix. Do not rename ERP-owned tables to satisfy this convention.
- Treat ERP table/column names, warehouse IDs, commodity/supplier mappings, status values, and `wms_erp_*` tables as cross-repository contracts.
- Before changing a shared entity or migration, trace the producing ERP code, WMS mapping/migration, downstream consumers, and rollout order. Prefer additive, repeatable, reversible migrations.
- Do not add a new ERP/WMS HTTP integration when the established shared-database contract already owns the flow unless the user explicitly approves an architecture change.

## Frontend and permissions

- Keep Controller request/response models synchronized with `frontend/src/api`, TypeScript types, pages, and error handling.
- WMS dynamic menus and permissions are handled by the WMS role/menu flow; do not assume ERP administration permissions are interchangeable.
- Frontend visibility is not a security boundary. Authorization and state validation belong on the backend.

## Verification

- Backend: prefer targeted tests in `backend/ModernWMS.Tests`, then solution build when applicable.
- Frontend: use the scripts declared in `frontend/package.json`; start with targeted unit/type/build checks and use E2E only when the task needs browser-level proof.
- Database initialization or migration execution can change data. Inspect the target and obtain explicit authority before running it.
- Never commit local secrets or environment-specific addresses from appsettings, user secrets, or `.env` files.

## Documentation

- Repository-owned technical details may stay in `docs/`; business notes at the repository root remain valid references until deliberately consolidated.
- Cross-repository understanding and current status go under `/mnt/d/ai-dev/doc/` and must link back to concrete code or repository documents.
- Do not use agentic-tools Memory as a mandatory message bus. If it is used as an auxiliary aid, Markdown remains the durable product record.

## Change discipline

- Preserve the large existing working tree and avoid broad formatting or line-ending changes.
- Follow the workspace automatic commit policy: after a completed change and its applicable verification, stage only task-owned files and commit them promptly with the suggested Chinese message.
