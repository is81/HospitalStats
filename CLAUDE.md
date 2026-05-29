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
npm run build                 # 类型检查 + 生产构建（含旧浏览器 polyfills）
```

**浏览器兼容**：`@vitejs/plugin-legacy` + `.browserslistrc` 配置支持 Chrome 64+ / Firefox 67+ / Safari 12+ / Edge 79+（2018+ 浏览器）。构建时自动注入 polyfills（ES2015+ 语法转换 + 缺失 API 补齐），无需单独引入。

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

**构建并发布前的必修操作**：`dotnet build` 和 `dotnet publish` 都会因 exe 被运行中的进程锁定而失败。发布前必须：
```bash
# 1. 先停后端进程
powershell -Command "Get-Process -Name 'HospitalStats.Api' -ErrorAction SilentlyContinue | Stop-Process -Force"
sleep 2
# 2. 构建前端（如需要）
cd F:\HospitalStats\hospital-stats-frontend && npm run build
# 3. 发布后端
dotnet publish "F:/HospitalStats/HospitalStats.Backend/HospitalStats.Api" -c Release -o "F:/HospitalStats/deploy/publish" --no-restore
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
6. 执行时 → **Count SQL 用 rawSql 包裹子查询**（计数不需要中文），**Data SQL 走配置路径**（Fields/Joins/Filters 拼装），这样 US7ASCII 数据库能自动对字符串列加 `RAWTOHEX(UTL_RAW.CAST_TO_RAW())` 包裹。仅当配置没有任何 Fields 时才退回到 rawSql 路径做 Data SQL

### Oracle 10g 注意事项

- 无 `OFFSET/FETCH`，必须用 `ROWNUM` 三层子查询分页
- 老医院系统常见 `US7ASCII` 字符集——数据源的 `CharSetOverride` 和 `ExecuteAsync` 中的编码转换逻辑处理
- Oracle 连接串在 SQLite 中以 AES-CBC 加密存储，运行时解密

### US7ASCII 中文编码处理（关键）

**问题根因**：US7ASCII 字符集只支持 0x00-0x7F 字节。Oracle 传输层会将超范围字节替换为 `?`，中文数据经 ODP.NET 到达应用层时已损坏。

**解决策略——双路径分工**：

| 路径 | Count SQL | Data SQL |
|------|-----------|----------|
| 有 rawSql | rawSql + 筛选注入 | rawSql + hex 包装 + 筛选注入 |
| 无 rawSql | 配置路径 | 配置路径（自动 RAWTOHEX） |

**核心机制**：
1. **Count SQL**：rawSql 包裹为 `SELECT COUNT(*) FROM (<rawSql>) "_cnt"`，筛选条件通过 `InjectWhereIntoRawSql` 注入到 GROUP BY/ORDER BY 之前
2. **Data SQL**：rawSql 外层包 hex 编码 SELECT（`HexEncodeRawSqlColumns`），对 string 类型输出列包裹 `RAWTOHEX(UTL_RAW.CAST_TO_RAW("col"))`，再套 ROWNUM 分页。筛选注入同理
3. **ParseSelectAliases**：从 rawSql SELECT 子句中提取输出列别名，用于 hex 包装时匹配 string 列

#### 向 US7ASCII 库插入中文测试数据

US7ASCII 不接受 GBK 中文字节，直接用 `INSERT`/`UPDATE` 写中文会变成 `?`。必须用 `UTL_RAW.CAST_TO_VARCHAR2(HEXTORAW('<GBK_HEX>'))` 写入原始 GBK 字节。

**PowerShell 生成 GBK HEX 并执行**：

```bash
# 1. 计算中文的 GBK HEX
$enc = [System.Text.Encoding]::GetEncoding(936)
$bytes = $enc.GetBytes("阿莫西林")
[BitConverter]::ToString($bytes).Replace('-', '')
# 输出: B0A2C4AACEF7C1D6

# 2. 写入 UPDATE 语句（UTF8 no BOM）
$sql = "UPDATE OUTP_BILL_ITEMS SET ITEM_NAME = UTL_RAW.CAST_TO_VARCHAR2(HEXTORAW('B0A2C4AACEF7C1D6')) WHERE ITEM_CODE = 'M001';`nEXIT;"
[System.IO.File]::WriteAllText("C:\Temp\upd.sql", $sql, [System.Text.UTF8Encoding]::new($false))

# 3. 执行（无需特殊 NLS_LANG）
sqlplus -S "outpbill/outpbill@ORCL" "@C:\Temp\upd.sql"
```

**验证存储正确性**（查 HEX 而非直接读中文）：
```sql
SELECT ITEM_CODE, RAWTOHEX(UTL_RAW.CAST_TO_RAW(ITEM_NAME)) hex_name FROM OUTP_BILL_ITEMS;
```

应用层通过数据源 `CharSetOverride=gbk` + `QueryExecutionService` 的 hex 解码逻辑自动还原为中文显示，无需额外处理。

### 数据范围权限

- **上下文筛选器**（`QueryFilter.IsContextFilter` + `ContextKey`）：配置查询时标记某个筛选条件为"按用户身份自动填充"。支持 `DeptName`（从 `DEPT_DICT` 获取科室名）、`UserId`（当前用户 ID）。
- `QueryExecutionService.BuildWhereClause` 检测 `IsContextFilter=true`，从 `IHttpContextAccessor` 读取 JWT Claims（`dept_name`、`nameid`）自动注入值，用户不可见、不可覆盖。缓存键包含上下文值以隔离不同用户。
- `AuthController` 登录时签发 `dept_name` claim（用户 `DeptName` 不为空时），`QueryView.vue` 中 `visibleFilters` 计算属性过滤隐藏上下文筛选器。
- `GET /api/admin/dept-options` 从 Oracle `DEPT_DICT` 表动态获取科室名称列表（按 `SERIAL_NO` 排序），供用户管理页面下拉选择。

## 生产部署

### 完整发布流程

```bash
# 1. 停后端
powershell -Command "Get-Process -Name 'HospitalStats.Api' -ErrorAction SilentlyContinue | Stop-Process -Force"
sleep 2

# 2. 构建前端（输出到 wwwroot/）
cd F:\HospitalStats\hospital-stats-frontend
npm run build

# 3. 发布后端（含 wwwroot/）
dotnet publish "F:/HospitalStats/HospitalStats.Backend/HospitalStats.Api" -c Release -o "F:/HospitalStats/deploy/publish" --no-restore

# 4. 同步 config.db（如需）
cp F:/HospitalStats/deploy/publish/config.db F:/HospitalStats/HospitalStats.Backend/HospitalStats.Api/config.db

# 5. 启动（Production 模式 = API + 静态文件）
export ASPNETCORE_ENVIRONMENT=Production
cd F:/HospitalStats/deploy/publish && dotnet HospitalStats.Api.dll --urls http://0.0.0.0:5000
```

单端口 5000 同时提供前端 SPA 和 `/api` 后端。

### 构建

前端 `npm run build` 直接输出到 `wwwroot/`（`vite.config.ts` 已配置 `outDir`，`emptyOutDir: true`）。**注意：构建输出是 `wwwroot/` 而非 `dist/`，部署时务必从 `wwwroot/` 复制。**

后端 `dotnet publish` 会自动包含 `wwwroot/` 作为内容文件，因此先构建前端再 publish 后端即可一次性产出完整部署包：

```bash
cd F:\HospitalStats\hospital-stats-frontend
npm run build
cd F:\HospitalStats\HospitalStats.Backend\HospitalStats.Api
dotnet publish -c Release -o F:\HospitalStats\deploy\publish
```

如果仅更新前端，手动复制到部署目录：
```bash
cp -r F:/HospitalStats/HospitalStats.Backend/HospitalStats.Api/wwwroot/* F:/HospitalStats/deploy/publish/wwwroot/
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

## 项目日志

每次开发会话结束后，在 `F:\HospitalStats\logs\project-YYYY-MM-DD.md` 追加记录当日主要操作。格式：

```markdown
## 2026-05-27

- **做了什么** — 简述改动内容和原因
- **涉及文件** — 列出修改的关键文件
- **部署状态** — 是否已部署到服务器
```

同一天多次会话追加到同一文件。日志文件命名按本地日期（北京时间）。

## 前端设计系统

使用 frontend-design skill（`F:\HospitalStats\.claude\skills\frontend-design`）进行 UI 设计时，遵循以下项目设计规范：

- **主色调**: Teal `#0d9488`（替代 Element Plus 默认蓝色），医疗数据平台的临床感、专业感
- **侧边栏**: 深 Slate `#0f172a`，文字 `#94a3b8`，激活态 Teal `#2dd4bf`
- **页面背景**: 暖灰 `#f1f5f9`
- **卡片/表面**: 纯白，阴影使用 slate 色调
- **字体栈**: `PingFang SC, Microsoft YaHei, Hiragino Sans GB, WenQuanYi Micro Hei, sans-serif`
- **圆角**: 基础 6px，卡片 8px，头像/图标 10px
- **主题变量文件**: `src/styles/theme.css` — 覆盖 Element Plus CSS 变量
- **设计方向**: "Clinical Precision" — 克制、专业、信赖感，避免花哨，强调信息层次

新建或美化页面时必须遵循此设计系统，确保风格统一。