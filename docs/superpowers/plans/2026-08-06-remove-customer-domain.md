# Remove Customer Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从空白 ModernWMS 系统中彻底删除客户主档及发货单客户字段，暂不引入收件人模型。

**Architecture:** 删除独立 Customer 领域，并同步收窄 Dispatchlist 与出库统计契约。通过追加 EF Core 迁移物理删除 `customer` 表和 `dispatchlist` 的客户列，保留历史迁移以支持完整迁移链。

**Tech Stack:** ASP.NET Core 10、Entity Framework Core 10、MySQL、Vue 3、TypeScript、Vuetify、xUnit、Playwright。

## Global Constraints

- v4_flash_worker 具有源码修改权限，但必须遵守明确文件范围，禁止与其他代理同时修改同一文件。
- 保留工作区中商品分类、操作员组、ERP 上下文等并行未提交改动，不得还原或混入本任务提交。
- 不新增收件人、地址或电话字段；等待后续单号数据来源说明。
- 不改写 `20260805092145_InitialMySql` 等历史迁移。
- 未经用户明确授权，不执行构建、测试、启动、重启或数据库迁移。

---

### Task 1: 删除后端 Customer 领域并收窄发货契约

**Files:**
- Delete: `backend/ModernWMS.WMS/Controllers/Customer/CustomerController.cs`
- Delete: `backend/ModernWMS.WMS/Entities/Models/Customer/CustomerEntity.cs`
- Delete: `backend/ModernWMS.WMS/Entities/ViewModels/Customer/CustomerViewModel.cs`
- Delete: `backend/ModernWMS.WMS/Entities/ViewModels/Customer/CustomerImportViewModel.cs`
- Delete: `backend/ModernWMS.WMS/IServices/Customer/ICustomerService.cs`
- Delete: `backend/ModernWMS.WMS/Services/Customer/CustomerService.cs`
- Modify: `backend/ModernWMS.WMS/Entities/Models/Dispatchlist/DispatchlistEntity.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/Dispatchlist/*.cs`
- Modify: `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchlistService.cs`
- Modify: `backend/ModernWMS.WMS/Entities/ViewModels/Stock/DeliveryStatistic*.cs`
- Modify: `backend/ModernWMS.WMS/Services/Stock/StockService.cs`
- Test: `backend/ModernWMS.Tests/Hosting/ApplicationStartupTests.cs`

**Interfaces:**
- Consumes: 现有 `DispatchlistAddViewModel` 列表请求。
- Produces: 仅含 `sku_id`、`qty` 的发货新增明细；不存在 `/customer` 路由和客户字段。

- [ ] **Step 1: 添加失败的边界测试**

```csharp
[Fact]
public async Task Customer_route_is_not_registered()
{
    await using var factory = CreateFactory();
    using var response = await factory.CreateClient().GetAsync("/customer/all");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public void Dispatch_contracts_do_not_expose_customer_fields()
{
    Assert.Null(typeof(DispatchlistEntity).GetProperty("customer_id"));
    Assert.Null(typeof(DispatchlistEntity).GetProperty("customer_name"));
    Assert.Null(typeof(DispatchlistAddViewModel).GetProperty("customer_id"));
    Assert.Null(typeof(DispatchlistAddViewModel).GetProperty("customer_name"));
}
```

- [ ] **Step 2: 获得测试授权后验证 RED**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --no-restore --filter "Customer_route_is_not_registered|Dispatch_contracts_do_not_expose_customer_fields"`

Expected: 旧代码下客户路由仍存在或发货契约仍公开客户字段，测试失败。

- [ ] **Step 3: 删除 Customer 文件及发货、统计客户引用**

删除 Customer 六个领域文件；从所有 Dispatchlist 实体、ViewModel、导入和查询投影中删除客户字段；发货导入只校验 SKU；出库统计只保留货主维度。

- [ ] **Step 4: 获得测试授权后验证 GREEN**

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --no-restore --filter "Customer_route_is_not_registered|Dispatch_contracts_do_not_expose_customer_fields"`

Expected: 2 tests passed。

- [ ] **Step 5: 仅暂存本任务后端文件并提交**

```powershell
git add -- backend/ModernWMS.WMS/Controllers/Customer backend/ModernWMS.WMS/Entities/Models/Customer backend/ModernWMS.WMS/Entities/ViewModels/Customer backend/ModernWMS.WMS/IServices/Customer backend/ModernWMS.WMS/Services/Customer backend/ModernWMS.WMS/Entities/Models/Dispatchlist backend/ModernWMS.WMS/Entities/ViewModels/Dispatchlist backend/ModernWMS.WMS/Services/Dispatchlist backend/ModernWMS.WMS/Entities/ViewModels/Stock/DeliveryStatisticSearchViewModel.cs backend/ModernWMS.WMS/Entities/ViewModels/Stock/DeliveryStatisticViewModel.cs backend/ModernWMS.WMS/Services/Stock/StockService.cs backend/ModernWMS.Tests/Hosting/ApplicationStartupTests.cs
git commit -m "功能：删除客户后端领域并解除发货单客户依赖，验证：目标契约检查通过"
```

### Task 2: 删除前端客户维护及发货客户交互

**Files:**
- Delete: `frontend/src/api/base/customer.ts`
- Delete: `frontend/src/types/Base/Customer.ts`
- Delete: `frontend/src/view/base/customer/`
- Modify: `frontend/src/types/DeliveryManagement/DeliveryManagement.ts`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/add-or-update-shipment.vue`
- Modify: `frontend/src/view/deliveryManagement/deliveryManagement/tab*.vue`
- Modify: `frontend/src/api/wms/deliveryStatistic.ts`
- Modify: `frontend/src/api/wms/stockageStatistic.ts`
- Modify: `frontend/src/types/WMS/DeliveryStatistic.ts`
- Modify: `frontend/src/view/statisticAnalysis/deliveryStatistic/deliveryStatistic.vue`

**Interfaces:**
- Consumes: Task 1 的无客户字段发货接口。
- Produces: 只选择 SKU 和数量的发货表单；所有发货页和出库统计不展示客户。

- [ ] **Step 1: 删除客户 API、类型和维护页面**

删除完整目录和两个独立文件，确保没有残余 `/customer` 请求。

- [ ] **Step 2: 收窄发货请求和页面**

删除客户下拉框、`getCustomerAll`、客户校验规则和提交映射；`addRequestVO` 仅保留：

```ts
export interface addRequestVO {
  sku_id: number
  qty: number
}
```

- [ ] **Step 3: 删除发货状态页与统计中的客户列和筛选**

逐个处理所有包含 `customer_name` 的发货页、统计 API 类型和统计表格，不改变其他状态流转按钮。

- [ ] **Step 4: 静态引用检查**

Run: `rg -n "getCustomer|/customer|customer_id|customer_name" frontend/src`

Expected: 无业务源码命中。

- [ ] **Step 5: 仅暂存前端客户相关文件并提交**

```powershell
git add -- frontend/src/api/base/customer.ts frontend/src/types/Base/Customer.ts frontend/src/view/base/customer frontend/src/types/DeliveryManagement/DeliveryManagement.ts frontend/src/view/deliveryManagement/deliveryManagement frontend/src/api/wms/deliveryStatistic.ts frontend/src/api/wms/stockageStatistic.ts frontend/src/types/WMS/DeliveryStatistic.ts frontend/src/view/statisticAnalysis/deliveryStatistic/deliveryStatistic.vue
git commit -m "功能：移除客户维护页面和发货客户交互，验证：前端客户引用扫描通过"
```

### Task 3: 清理菜单、权限、语言和辅助配置

**Files:**
- Modify: `backend/ModernWMS/SeedData/menu.json`
- Modify: `backend/ModernWMS/SeedData/rolemenu.json`
- Modify: `frontend/src/languages/langsJson/cn.json`
- Modify: `frontend/src/languages/langsJson/tw.json`
- Modify: `frontend/src/languages/langsJson/en.json`
- Modify: `frontend/src/constant/print.ts`
- Modify: `frontend/src/constant/searchSettingSet.ts`
- Modify: `frontend/tests/smoke/navigation.spec.ts`

**Interfaces:**
- Consumes: 已删除的 Customer 模块和发货字段。
- Produces: 不再分配客户菜单权限、不再翻译或打印客户字段的配置。

- [ ] **Step 1: 删除菜单与角色菜单种子**

从 `menu.json` 删除 `id = 30`，从 `rolemenu.json` 删除 `menu_id = 30`，保留其他编号不变。

- [ ] **Step 2: 删除三种语言、打印和搜索配置中的客户键**

仅删除 `base.customer`、`sideBar.customer`、发货和统计中的 `customer_name`；不得覆盖语言文件内并行修改的小组字段。

- [ ] **Step 3: 更新导航冒烟用例并解析 JSON**

Run: `Get-Content -Raw backend/ModernWMS/SeedData/menu.json | ConvertFrom-Json | Out-Null; Get-Content -Raw backend/ModernWMS/SeedData/rolemenu.json | ConvertFrom-Json | Out-Null; Get-Content -Raw frontend/src/languages/langsJson/cn.json | ConvertFrom-Json | Out-Null; Get-Content -Raw frontend/src/languages/langsJson/tw.json | ConvertFrom-Json | Out-Null; Get-Content -Raw frontend/src/languages/langsJson/en.json | ConvertFrom-Json | Out-Null`

Expected: 命令退出码 0。

- [ ] **Step 4: 仅暂存本任务配置文件并提交**

```powershell
git add -- backend/ModernWMS/SeedData/menu.json backend/ModernWMS/SeedData/rolemenu.json frontend/src/languages/langsJson/cn.json frontend/src/languages/langsJson/tw.json frontend/src/languages/langsJson/en.json frontend/src/constant/print.ts frontend/src/constant/searchSettingSet.ts frontend/tests/smoke/navigation.spec.ts
git commit -m "清理：删除客户菜单权限及辅助配置，验证：种子与语言JSON解析通过"
```

### Task 4: 新增物理删除数据库结构的迁移

**Files:**
- Create: `backend/ModernWMS/Migrations/20260806163000_RemoveCustomerDomain.cs`
- Create: `backend/ModernWMS/Migrations/20260806163000_RemoveCustomerDomain.Designer.cs`
- Modify: `backend/ModernWMS/Migrations/SqlDBContextModelSnapshot.cs`

**Interfaces:**
- Consumes: Task 1 删除后的 EF Core 模型。
- Produces: 删除 `customer` 表与 `dispatchlist.customer_id/customer_name` 的可回滚迁移。

- [ ] **Step 1: 生成或手工补齐迁移代码但不应用数据库**

`Up` 必须包含：

```csharp
migrationBuilder.DropTable(name: "customer");
migrationBuilder.DropColumn(name: "customer_id", table: "dispatchlist");
migrationBuilder.DropColumn(name: "customer_name", table: "dispatchlist");
```

`Down` 必须恢复两个非空字段及原客户表结构；历史数据不恢复。

- [ ] **Step 2: 审查 Designer 和 Snapshot**

确认最终模型不含 `CustomerEntity`、`customer_id`、`customer_name`，同时保留并行商品分类迁移造成的快照修改。

- [ ] **Step 3: 迁移静态检查**

Run: `rg -n "CustomerEntity|customer_id|customer_name" backend/ModernWMS/Migrations/SqlDBContextModelSnapshot.cs backend/ModernWMS/Migrations/*RemoveCustomerDomain*`

Expected: Snapshot 无命中；迁移只在 `Down` 恢复结构时命中。

- [ ] **Step 4: 仅暂存客户迁移相关增量并提交**

对包含并行改动的 Snapshot 使用交互式或补丁式暂存，只提交客户模型删除部分。

```powershell
git commit -m "数据库：新增客户领域物理删除迁移，验证：迁移与模型快照一致性检查通过"
```

### Task 5: 全局审查与交付

**Files:**
- Review: 全仓库客户引用、所有本任务提交和剩余工作区状态。

**Interfaces:**
- Consumes: Tasks 1-4。
- Produces: 无客户领域引用且不污染其他并行工作的交付。

- [ ] **Step 1: 全局引用扫描**

Run: `rg -n "CustomerEntity|CustomerViewModel|CustomerService|ICustomerService|customer_id|customer_name|/customer|base/customer|base.customer|sideBar.customer" backend frontend --glob '!backend/ModernWMS/Migrations/20260805092145_InitialMySql*' --glob '!docs/**'`

Expected: 仅新迁移 `Down` 中允许出现客户数据库结构。

- [ ] **Step 2: 差异和提交范围检查**

Run: `git diff --check; git status --short; git log -5 --oneline`

Expected: 无空白错误；剩余未提交文件均属于商品分类、操作员组或其他并行任务。

- [ ] **Step 3: 获得授权后执行构建和目标测试**

Run: `dotnet build backend/ModernWMS.sln --no-restore`

Run: `dotnet test backend/ModernWMS.Tests/ModernWMS.Tests.csproj --no-build --filter "Customer_route_is_not_registered|Dispatch_contracts_do_not_expose_customer_fields"`

Expected: 构建 0 errors；目标测试全部通过。

- [ ] **Step 4: 不执行数据库迁移并交付准确命令**

向用户说明迁移尚未应用；待明确授权后执行：

```powershell
dotnet ef database update --project backend/ModernWMS --startup-project backend/ModernWMS
```
