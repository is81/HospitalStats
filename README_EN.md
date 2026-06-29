# Hospital Statistics Platform <sup style="color:#2dd4bf;font-size:12px;font-weight:600;margin-left:4px">Community</sup>

A data query and statistics platform for hospital HIS systems. **Community Edition**, MIT open source, free forever. Supports Oracle 10g/11g (including US7ASCII charset), SQL import, UNION complex queries, dynamic filtering, and ECharts dashboards.

> 💡 For advanced features like pivot tables, drill-down, scheduled reports, DRG/DIP analysis, or SSO/LDAP integration, check out the [Enterprise Edition](https://github.com/is81/HospitalStats) (contact the project owner).

## Tech Stack

| Layer | Technology |
|-------|------------|
| Frontend | Vue 3 / Vite / Element Plus / ECharts / Pinia |
| Backend | .NET 8 / ASP.NET Core Web API / Dapper / EF Core |
| Database | Oracle 10g/11g (data sources) + SQLite (config store) |
| Auth | JWT Bearer + BCrypt |
| Browser Support | Chrome 64+ / Firefox 67+ / Safari 12+ / Edge 79+ (2018+) |

## Features

- **SQL Import & Parsing**: Paste Oracle SQL to auto-extract columns, filters, and JOINs. Supports UNION queries.
- **Dynamic Querying**: 12 filter operators (=, ≠, ＞, ＜, LIKE, NOT LIKE, IN, NOT IN, BETWEEN, etc.) injected per UNION branch.
- **US7ASCII Support**: Hex-encode string columns for transport-layer safety, Chinese alias rewriting (`_cN`/`_cxN`) to prevent ORA-00918, three-tier column type matching.
- **Dashboard**: Flexible grid card layout, number/bar/line/pie charts, date range filter, drag-to-reorder.
- **RBAC**: Role-based menu access, context filters (auto-inject department/user from JWT).
- **Data Sources**: Multi-Oracle connection management, AES-CBC encrypted connection strings, live connection test.
- **Metadata Scanner**: Automatic Oracle schema discovery, table/column browsing, Chinese alias editing.
- **Excel Export**: Query result export with automatic hex-decoding for string columns.
- **Audit Log**: User operation logging to `logs/audit-{date}.log`.
- **System Settings**: Runtime-configurable query timeout and max row limit.
- **Auto Backup**: Periodic SQLite config database backups.

## Project Structure

```
HospitalStats/
├── hospital-stats-frontend/      # Vue 3 + Vite frontend
│   └── src/
│       ├── views/                 # Page components (admin/dashboard/datasources/meta/query)
│       ├── components/            # Shared components
│       ├── api/                   # Axios API wrappers
│       ├── stores/                # Pinia state management
│       ├── layout/                # Layout components
│       ├── router/                # Route definitions
│       ├── assets/                # Static assets
│       └── styles/                # Theme CSS variables
├── HospitalStats.Backend/
│   ├── HospitalStats.Api/         # .NET 8 Web API
│   │   ├── Controllers/           # 8 controllers
│   │   ├── Services/              # Core service layer
│   │   ├── Models/                # Entity models (13)
│   │   ├── DTOs/                  # Request/response DTOs
│   │   ├── Data/                  # EF Core DbContext (15 DbSets)
│   │   └── Middleware/            # Exception / audit
│   └── HospitalStats.Api.Tests/   # xUnit test suite (180 tests)
├── deploy/                        # Deployment scripts & publish output
├── docs/                          # Local documentation (not uploaded)
├── LICENSE                        # MIT License
├── CLA.md                         # Contributor License Agreement
├── CONTRIBUTING.md                # Contribution guide
└── README.md
```

## Quick Start

### Development

```bash
# Backend
cd HospitalStats.Backend/HospitalStats.Api
dotnet run

# Frontend
cd hospital-stats-frontend
npm install
npm run dev
```

### Production Deployment

```bash
# 1. Build frontend (output to wwwroot/)
cd hospital-stats-frontend && npm run build

# 2. Publish backend (includes frontend static files)
dotnet publish HospitalStats.Backend/HospitalStats.Api -c Release -o ./deploy/publish

# 3. Set environment variables
export ASPNETCORE_ENVIRONMENT=Production
export Jwt__Key=<32-char random string>
export Encryption__Key=<16-char random string>

# 4. Start
cd deploy/publish && dotnet HospitalStats.Api.dll --urls http://0.0.0.0:5000
```

Default admin account `admin` with a randomly generated password printed to console on first startup. Change immediately after logging in.

### Important

- Always override `Jwt:Key` and `Encryption:Key` in production.
- The SQLite config database and default admin are created automatically on first run.
- Oracle connection strings are configured via the admin UI and stored with AES encryption.
- Set charset override to `gbk` for US7ASCII data sources.

## Oracle 10g Considerations

- Pagination uses `ROWNUM` three-level nested subqueries (`OFFSET/FETCH` not supported).
- US7ASCII Chinese text is safely transported via `RAWTOHEX(UTL_RAW.CAST_TO_RAW())`.
- UNION filter injection is per-branch to avoid ORA-00918 ambiguity.
- Inline Chinese literals use `RAWTOHEX(HEXTORAW())` encoding.

## Extensibility

Currently Oracle-only, but the architecture supports extension to other databases (SQL Server, PostgreSQL, etc.) via an `IDbAdapter` interface. Oracle-specific logic would be extracted into an adapter, injected per data source:

| Current Oracle-specific logic | Adapter method |
|------------------------------|----------------|
| `ROWNUM` three-level pagination | `BuildPagedQuery(sql, page, pageSize)` |
| `TO_DATE()` formatting | `FormatDateParam(value)` |
| `RAWTOHEX(UTL_RAW.CAST_TO_RAW())` | `EncodeStringColumn(col)` |
| `ALL_TABLES` / `ALL_TAB_COLUMNS` scanning | `GetSchemaMetadata(conn)` |

Effort: ~5-6 days backend for the first new database, zero frontend changes. Existing Oracle users unaffected.

## Contributing

Community Edition is in maintenance mode — only bug fixes and documentation improvements are accepted. New features belong in the Enterprise Edition. Before submitting a PR:

- [Contribution Guide (CONTRIBUTING.md)](CONTRIBUTING.md) — dev setup, branch strategy, code style
- [Contributor License Agreement (CLA.md)](CLA.md) — must be agreed before PR submission
- [Enterprise Development Guide (中文)](docs/企业版开发规范.md) — community/enterprise code isolation rules

## License

MIT License

## Acknowledgments

This project was developed with the assistance of [Claude Code](https://claude.ai/code) (by Anthropic) and the DeepSeek large language model.
