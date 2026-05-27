# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 提供项目上下文指导。

## 开发命令

**后端** (`.NET 8`, `F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api`):
```bash
cd F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api
dotnet run                    # 启动，监听 http://localhost:5000
dotnet build --no-restore     # 快速编译检查
```

**前端** (Vue 3 + Vite, `F:\HospitalStats\hospital-stats-frontend`):
```bash
cd F:\HospitalStats\hospital-stats-frontend
npm run dev                   # 启动，监听 http://localhost:5173，/api 代理到 localhost:5000
npm run build                 # 类型检查 + 生产构建
```

**结束后端进程**（exe 常被 dotnet 进程锁定）:
```bash
powershell -Command "Get-Process -Name 'HospitalStats.Api' -ErrorAction SilentlyContinue | Stop-Process -Force"
```

**修改后端 .cs 文件前的必须操作**：先停后端进程再保存文件，否则 `dotnet build` 会因 exe 被锁定而失败。标准流程：
```bash
powershell -Command "Get-Process -Name 'HospitalStats.Api' -ErrorAction SilentlyContinue | Stop-Process -Force"
sleep 2
# 修改 .cs 文件
dotnet build --no-restore
dotnet run --urls http://0.0.0.0:5000 &
```

**SQLite 快速查询**:
```bash
cd F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api
sqlite3 config.db "SELECT * FROM QueryConfigs;"
```

**测试** (xUnit, `F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api.Tests`):
```bash
dotnet test F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api.Tests --no-restore
# 或从测试项目目录:
cd F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api.Tests
dotnet test
```

测试覆盖两个核心服务：`QueryExecutionService`（运算符转 SQL、WHERE 构建、上下文筛选器注入、ROWNUM 分页、缓存键）和 `SqlParsingService`（SQL 注释清除、表/列/筛选/JOIN 解析、条件分割）。测试方法通过 `InternalsVisibleTo` 访问内部 API。

## 架构概览

**医院数据统计平台**：Vue 3 前端 → .NET 8 API → Oracle 10g/11g 数据源 + SQLite 配置库。

### 双数据库设计

- **SQLite** (`config.db`，位于 API 项目根目录)：存储所有配置——用户、角色、数据源（连接串加密存储）、扫描的元数据（MetaTable/MetaColumn）、查询配置、菜单。默认管理员 `admin`/`admin123`，由 `Program.cs` 种子逻辑创建。使用 `EnsureCreated()` 而非 Migration，新增字段需手动 `ALTER TABLE`。
- **Oracle**：实际的 HIS 数据库。通过 Dapper + `Oracle.ManagedDataAccess.Core` 直接执行查询。兼容 Oracle 10g，必须使用 `ROWNUM` 三层嵌套子查询分页（不支持 `OFFSET/FETCH`），日期参数用 `TO_DATE()`。

### 后端结构

- `Controllers/` — 7 个控制器：
  - `AuthController` — 登录获取 JWT Token
  - `AdminController` — 用户/角色 CRUD
  - `DataSourcesController` — 数据源 CRUD + 连接测试
  - `MetaController` — 扫描的元数据表/字段浏览
  - `QueryController` — 菜单树 + 查询配置 CRUD + SQL 解析
  - `QueryExecuteController` — 执行查询 + 导出 Excel
  - `DashboardController` — 仪表盘卡片配置
- `Services/`:
  - `DataSourceService` — 数据源 CRUD + AES-CBC 加密/解密连接串（SHA256 密钥，零 IV）
  - `MetaScannerService` — 扫描 Oracle 库表结构（`ALL_TABLES`、`ALL_TAB_COLUMNS`）填充 MetaTable/MetaColumn
  - `QueryExecutionService` — 动态 SQL 生成与执行。**优先使用 RawSql**：将原始 SQL 包裹为 `SELECT COUNT(*) FROM (<rawSql>) "_cnt"` 做计数，`SELECT * FROM (SELECT t.*, ROWNUM rn FROM (<rawSql>) t WHERE ROWNUM <= :endRow) WHERE rn >= :startRow` 做分页。无 RawSql 时才从配置部件拼装 SQL（fields→SELECT，joins→按表分组合并 ON 条件，filters→带参数化运算符的 WHERE）。支持 `NOT LIKE`/`NOT IN`/`NOT BETWEEN`
  - `SqlParsingService` — 基于正则的 Oracle SQL 解析器：提取 SELECT 列、FROM 表、WHERE 筛选条件（含 `NOT LIKE`/`NOT IN`/`NOT BETWEEN`）、简单 JOIN 条件（仅 `别名.列 = 别名.列`）、GROUP BY、ORDER BY。**函数包裹的 JOIN 条件**（如 `to_char(a.date)=to_char(b.date)`）解析器无法识别，依赖 RawSql 原样执行保证结果正确
- `Data/AppDbContext.cs` — EF Core 上下文，12 个 DbSet，完整关系配置（唯一索引、级联行为）
- `Models/` — 12 个实体模型，对应 SQLite 表结构
- `DTOs/QueryDto.cs` — 所有请求/响应 DTO，含 SQL 导入相关类型

### 认证与授权

- **JWT Bearer** 认证 + BCrypt 密码哈希
- **基于角色的访问控制**：种子创建 `admin` 角色。前端路由守卫检查 `roles.includes('admin')` 拦截管理员页面。后端控制器类级别使用 `[Authorize]`
- **菜单权限过滤**：`GetMenus()` 根据用户角色的 `RoleMenu` 记录过滤菜单树。新菜单在 `CreateMenu` 中自动分配给所有角色

### 前端结构

- `src/router/index.ts` — 路由定义，`meta: { admin: true }` 为管理员页面，`meta: { noAuth: true }` 为登录页。路由守卫检查 Token + 管理员角色
- `src/api/index.ts` — Axios 实例，baseURL 为 `/api`，自动附加 JWT Token，401 时跳转登录
- `src/stores/auth.ts` — Pinia 状态：token、displayName、roles、menuIds，持久化到 localStorage
- `src/layout/MainLayout.vue` — 侧边栏导航，管理员菜单项通过 `authStore.isAdmin` 控制显示
- 关键页面：`ConfigEdit.vue`（5 步向导 + SQL 导入模式切换）、`ConfigList.vue`（查询配置列表）、`MenuManage.vue`（树形表格菜单编辑）、`QueryView.vue`（数据表格 + 分页 + 筛选）、`DashboardHome.vue`（ECharts 仪表盘）

### SQL 导入流程

1. 用户粘贴 Oracle SQL → `POST /api/query/configs/parse-sql`
2. `SqlParsingService` 正则解析列、筛选（含 NOT LIKE/NOT IN/NOT BETWEEN）、简单 JOIN、排序/分组。返回 `SqlParseResponse`，每项标注匹配状态，同时返回**清理后的原始 SQL** 存入 `rawSql`
3. 前端展示预览面板（绿色 = 已匹配，黄色 = 未匹配）
4. 用户点击"应用并继续" → 表单填充解析出的字段/筛选/JOIN，同时保存 `rawSql`
5. 保存时 → `QueryConfig.RawSql` 持久化到 SQLite
6. 执行时 → `QueryExecutionService` 直接用 `RawSql` 套 ROWNUM 分页执行，**不再拆解重建**。保证复杂表达式（`to_char()`、`DECODE()`、`CASE WHEN`、函数式 JOIN）结果与原始 SQL 完全一致

### Oracle 10g 注意事项

- 无 `OFFSET/FETCH`，必须用 `ROWNUM` 三层子查询分页
- 老医院系统常见 `US7ASCII` 字符集——数据源的 `CharSetOverride` 和 `ExecuteAsync` 中的编码转换逻辑处理
- Oracle 连接串在 SQLite 中以 AES-CBC 加密存储，运行时解密

### 数据范围权限

- **上下文筛选器**（`QueryFilter.IsContextFilter` + `ContextKey`）：配置查询时标记某个筛选条件为"按用户身份自动填充"。支持 `DeptName`（从 `DEPT_DICT` 获取科室名）、`UserId`（当前用户 ID）。
- `QueryExecutionService.BuildWhereClause` 检测 `IsContextFilter=true`，从 `IHttpContextAccessor` 读取 JWT Claims（`dept_name`、`nameid`）自动注入值，用户不可见、不可覆盖。缓存键包含上下文值以隔离不同用户。
- `AuthController` 登录时签发 `dept_name` claim（用户 `DeptName` 不为空时），`QueryView.vue` 中 `visibleFilters` 计算属性过滤隐藏上下文筛选器。
- `GET /api/admin/dept-options` 从 Oracle `DEPT_DICT` 表动态获取科室名称列表（按 `SERIAL_NO` 排序），供用户管理页面下拉选择。

## 生产部署

### 构建

前端 `npm run build` 直接输出到 `wwwroot/`（`vite.config.ts` 已配置 `outDir`）。后端在非 Development 模式下自动服务静态文件 + SPA fallback：

```bash
cd F:\HospitalStats\hospital-stats-frontend
npm run build
cd F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api
dotnet publish -c Release -o ./publish
```

### 必须配置的环境变量

生产环境通过环境变量注入密钥（`appsettings.json` 中的默认值仅开发用，启动时有 Warning 日志提醒）：

| 变量 | 用途 | 要求 |
|------|------|------|
| `ASPNETCORE_ENVIRONMENT` | 设为 `Production` | 启用静态文件服务、收紧 CORS |
| `Jwt__Key` | JWT 签名密钥 | 至少 32 字符随机字符串 |
| `Encryption__Key` | Oracle 连接串 AES 加密密钥 | 至少 16 字符随机字符串 |
| `Cors__Origins` | 允许的前端域名 | 如 `https://your-domain.com` |

### 运行

```bash
cd publish
dotnet HospitalStats.Api.dll --urls http://0.0.0.0:5000
```

单端口 5000 同时提供前端 SPA 和 `/api` 后端。如需 HTTPS，建议前挂 nginx/IIS 处理 TLS 反代。

### 部署前检查清单

- 登录后修改默认 `admin` 密码（`admin123`）
- 确保 `config.db` 所在目录可写
- 生产 Oracle 数据源通过管理员页面配置，连接串自动 AES 加密存储

### config.db 自动备份

`ConfigDbBackupService` 后台服务定时备份 SQLite 数据库：

- 启动后 10 秒执行首次备份，之后按 `Backup:IntervalMinutes` 间隔执行（默认 60 分钟）
- 备份文件命名 `config_yyyyMMdd_HHmmss.db`，存储于 `backups/` 子目录
- 执行 WAL checkpoint 后文件复制，安全可靠
- 超过 `Backup:MaxCount`（默认 24）的旧备份自动删除

配置项 (`appsettings.json`)：
```json
{
  "Backup": {
    "IntervalMinutes": 60,
    "MaxCount": 24
  }
}
```
