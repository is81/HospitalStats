# 医院数据统计平台

面向医院 HIS 系统的数据查询与统计平台，支持 Oracle 10g/11g（含 US7ASCII 字符集），提供 SQL 导入、UNION 复杂查询、动态筛选、ECharts 仪表盘等功能。

## 技术栈

| 层 | 技术 |
|---|------|
| 前端 | Vue 3 / Vite / Element Plus / ECharts / Pinia |
| 后端 | .NET 8 / ASP.NET Core Web API / Dapper / EF Core |
| 数据库 | Oracle 10g/11g（数据源）+ SQLite（配置库） |
| 认证 | JWT Bearer + BCrypt |
| 兼容 | Chrome 64+ / Firefox 67+ / Safari 12+ / Edge 79+（2018+ 浏览器） |

## 功能

- **SQL 导入解析**：粘贴 Oracle SQL 自动提取列、筛选条件、JOIN 关系，支持 UNION 复杂查询
- **动态查询**：12 种筛选操作符（=、≠、＞、＜、LIKE、NOT LIKE、IN、NOT IN、BETWEEN 等），按分支独立注入
- **US7ASCII 适配**：字符串列 hex 编码/解码，中文别名安全化（`_cN`/`_cxN`），三层类型匹配
- **仪表盘**：8 卡片布局，数值/柱状图/折线图/饼图，日期筛选栏，支持拖拽配置
- **RBAC 权限**：基于角色的菜单访问控制，上下文筛选器（科室/用户自动注入）
- **数据源管理**：多 Oracle 库连接，AES-CBC 加密存储连接串，在线测试连接
- **元数据扫描**：Oracle Schema 自动发现，表/字段信息浏览，中文别名编辑
- **Excel 导出**：查询结果导出，支持 hex 编码列自动解码
- **审计日志**：记录用户操作到 `logs/audit-{date}.log`
- **配置管理**：查询超时、行数限制在线修改
- **自动备份**：SQLite 配置库定时备份

## 项目结构

```
HospitalStats/
├── hospital-stats-frontend/      # Vue 3 + Vite 前端
│   └── src/
│       ├── views/                 # 页面组件
│       ├── api/                   # Axios API 封装
│       ├── stores/                # Pinia 状态管理
│       ├── layout/                # 布局组件
│       ├── router/                # 路由配置
│       └── styles/                # 主题 CSS 变量
├── HospitalStats.Backend/
│   ├── HospitalStats.Api/         # .NET 8 Web API
│   │   ├── Controllers/           # 7 个控制器
│   │   ├── Services/              # 核心服务
│   │   ├── Models/                # 实体模型
│   │   ├── DTOs/                  # 请求/响应 DTO
│   │   ├── Data/                  # EF Core DbContext
│   │   └── Middleware/            # 异常/审计/授权中间件
│   ├── HospitalStats.Api.Tests/   # xUnit 测试
│   └── GenerateLicense/           # 激活码生成工具
├── docs/                          # 文档
├── deploy/publish/                # 发布输出
└── CLAUDE.md                      # AI 辅助开发指南
```

## 快速开始

### 开发环境

```bash
# 后端
cd HospitalStats.Backend/HospitalStats.Api
dotnet run

# 前端
cd hospital-stats-frontend
npm install
npm run dev
```

### 生产部署

```bash
# 1. 构建前端（输出到 wwwroot/）
cd hospital-stats-frontend && npm run build

# 2. 发布后端（含前端静态文件）
dotnet publish HospitalStats.Backend/HospitalStats.Api -c Release -o ./deploy/publish

# 3. 设置环境变量
export ASPNETCORE_ENVIRONMENT=Production
export Jwt__Key=<32位随机字符串>
export Encryption__Key=<16位随机字符串>

# 4. 启动
cd deploy/publish && dotnet HospitalStats.Api.dll --urls http://0.0.0.0:5000
```

默认管理员：`admin` / `admin123`（首次登录后务必修改）

### 注意事项

- 生产环境必须修改 `Jwt:Key` 和 `Encryption:Key` 环境变量
- 首次启动自动创建 SQLite 配置库和默认管理员
- Oracle 连接串在管理页面配置，AES 加密存储
- US7ASCII 数据源需在字符集覆盖中选择 `gbk`

## Oracle 10g 特殊处理

- 分页使用 `ROWNUM` 三层嵌套子查询（不支持 `OFFSET/FETCH`）
- US7ASCII 中文通过 `RAWTOHEX(UTL_RAW.CAST_TO_RAW())` 包装传输层
- UNION 查询按分支独立注入筛选，防 ORA-00918 中文别名冲突
- 行内中文字面量用 `RAWTOHEX(HEXTORAW())` 处理

## 许可证

MIT License
