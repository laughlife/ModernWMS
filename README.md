# ModernWMS

ModernWMS is a warehouse management system with a .NET backend and a Vue frontend. This fork has been modernized as an independent Windows-native project and does not use Docker.

For Chinese documentation, see [README.zh_CN.md](README.zh_CN.md).

## Technology stack

- .NET SDK 10.0.302 / ASP.NET Core 10 / Entity Framework Core 10
- MySQL 8.4
- Node.js 24.16.0 or newer / npm 11.17.0
- Vue 3.5 / TypeScript 6 / Vite 8 / Vuetify 4
- VXE Table 4 / ECharts 6 / Pinia 4 / Vue Router 5

## Prerequisites

- Windows 10/11 or a supported Windows Server release
- .NET SDK 10.0.302 (the repository `global.json` selects this SDK feature band)
- Node.js 24.16.0 or newer
- MySQL 8.4 available on `127.0.0.1:3306`

## Quick start

Configure the database connection and JWT signing key with .NET User Secrets. Do not commit real credentials to configuration files.

```powershell
dotnet user-secrets set "ConnectionStrings:MySqlConn" "Server=127.0.0.1;Port=3306;Database=wms;User ID=YOUR_USER;Password=YOUR_PASSWORD;Character Set=utf8mb4;" --project backend/ModernWMS
dotnet user-secrets set "TokenSettings:SigningKey" "REPLACE_WITH_AT_LEAST_32_UTF8_BYTES" --project backend/ModernWMS
dotnet run --project backend/ModernWMS
```

The backend starts at `http://localhost:21011`. Swagger is available at the application root and the health endpoint is `/health`.

In a second PowerShell window:

```powershell
cd frontend
npm ci
npm run dev
```

Open `http://127.0.0.1:80`. The deterministic baseline account is `admin` / `1`; change this password immediately outside a disposable local environment.

## Database initialization

The application applies EF Core migrations and inserts missing baseline records on startup. To initialize the database and exit:

```powershell
dotnet run --project backend/ModernWMS -- --initialize-database-only
```

Initialization is idempotent for the embedded baseline record IDs. See [docs/database.md](docs/database.md) for details.

## Verification

```powershell
dotnet restore backend/ModernWMS.sln
dotnet build backend/ModernWMS.sln --configuration Release --no-restore
dotnet test backend/ModernWMS.sln --configuration Release --no-build

cd frontend
npm ci
npm run test:unit
npm run build
npm run test:e2e
```

## Documentation

- [Development setup](docs/development.md)
- [Database initialization](docs/database.md)
- [Windows-native deployment](docs/deployment.md)
- [Modernization baseline](docs/baseline.md)
- [Upgrade plan](升级计划.md)

## License

Licensed under the [Apache License 2.0](LICENSE).
