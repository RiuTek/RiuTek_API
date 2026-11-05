# RiuTek API

[![CI & Publish to ACR](https://github.com/RiuTek/RiuTek_API/actions/workflows/ci-acr.yml/badge.svg)](https://github.com/RiuTek/RiuTek_API/actions/workflows/ci-acr.yml)

Backend for RiuTek, a personal e-commerce project for buying PC components and choosing parts for a custom build. Customers can browse products, check compatibility, save configurations and place orders through a conventional cart or Buy Now flow.

[Live website](https://riutek.store) · [Frontend repository](https://github.com/RiuTek/RiuTek_Web)

## Features

- **Accounts:** JWT authentication, rotating refresh tokens in HttpOnly cookies, profiles, delivery addresses and Customer/Staff/Admin permissions.
- **Catalog:** nested categories, nine component types, structured hardware specifications, keyword search, technical filters, sorting and pagination.
- **PC Builder:** anonymous compatibility previews, customer saved configurations, quantity-aware checks, build-to-cart and direct build checkout. Compatibility findings are advisory; price, availability and stock are checked separately when purchasing.
- **Cart and checkout:** authenticated persistent carts, version checks, checkout of selected cart items, Buy Now for individual products or saved builds, checkout quotes and idempotent order creation. Inactive carts are cleaned up after 30 days.
- **Orders and payments:** customer order history/cancellation, staff fulfillment actions, COD and Stripe Checkout in test mode. Signed Stripe webhooks handle payment events and stock restoration where applicable.
- **Inventory:** stock movement history and manual adjustments with concurrency checks. Creation, import, checkout, cancellation and payment expiration record their stock changes.
- **Product imports and media:** downloadable Excel templates, workbook inspection, validation before import and transactional create-only imports. Images uploaded to Cloudflare R2 can be mapped to workbook products by SKU.
- **Content:** post/comment APIs, purchased-product reviews with ratings and moderation, and homepage banner management. Banner images reuse the R2 upload flow; referenced media is protected from deletion.

Posts, comments and reviews are implemented as backend APIs; their customer-facing frontend screens are not part of the current web release. PNG export is implemented by the frontend in the browser, without an export API or stored image.

## Design

The backend is a modular monolith with Clean Architecture. Application features are organized as vertical slices using CQRS and MediatR.

```text
Core             Entities, component specifications and business rules
Application      Commands, queries, validators, DTOs and integration interfaces
Infrastructure   EF Core, PostgreSQL, Redis, authentication, Stripe and R2 adapters
API              HTTP contracts, controllers and application startup
```

Core does not depend on the outer layers. Application depends on Core; Infrastructure implements the interfaces used by Application. API composes the application and infrastructure services.

Expected business failures use Result/Error responses. FluentValidation runs in the MediatR pipeline, DTOs are mapped explicitly, and handlers enforce account/ownership checks in addition to endpoint authorization. Checkout revalidates prices and stock; idempotency and concurrency checks address repeated or competing requests.

## Technology

| Area | Stack |
| --- | --- |
| API | .NET 10, ASP.NET Core, C# |
| Application | MediatR, FluentValidation, CQRS |
| Database | PostgreSQL, EF Core 10, Npgsql |
| Authentication | JWT Bearer, BCrypt, refresh-token rotation |
| Integrations | Redis, Stripe Checkout, Cloudflare R2/S3 API, ClosedXML |
| Testing | xUnit, FluentAssertions, PostgreSQL Testcontainers |
| Deployment | Docker, GitHub Actions, Azure Container Registry, Azure App Service |

The database includes pgvector support for future embedding search. AI recommendations and semantic search are not implemented yet.

## Run locally

Requirements: .NET 10 SDK, PostgreSQL with the vector extension enabled, and EF Core CLI matching the project's EF Core version. Docker is needed for PostgreSQL integration tests; it is not required when using an existing local PostgreSQL instance.

From the repository root, configure a local database and a random JWT signing key in the terminal environment. The JWT key must contain at least 32 UTF-8 bytes; length alone does not guarantee randomness. Do not use production credentials for local development.

PowerShell 7.1 or newer:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = Read-Host "Local PostgreSQL connection string" -MaskInput
$env:JwtSettings__SecretKey = Read-Host "Random JWT signing key" -MaskInput
$env:RedisSettings__Enabled = "false"
$env:Stripe__Enabled = "false"
$env:R2__Enabled = "false"
$env:CartCleanup__Enabled = "false"

dotnet restore RiuTek_API.slnx
dotnet ef database update --project RiuTek.Infrastructure --startup-project RiuTek.API
dotnet run --project RiuTek.API --no-launch-profile
```

Install the matching dotnet-ef tool if it is not already available. Use the local URL printed by the application. In Development, the OpenAPI document is served at /openapi/v1.json. For refresh-cookie authentication, use HTTPS: refresh cookies are always Secure and HttpOnly. Do not weaken cookie settings to make local HTTP authentication work.

Environment variables set in this terminal apply to that process and its child processes; they do not permanently update User/Machine settings. Redis, Stripe and R2 are optional for local startup. Background cart cleanup is disabled in the example to avoid a startup sweep during setup.

### Optional integrations

Configure these only when enabling the corresponding integration:

| Service | Environment variable names |
| --- | --- |
| Redis | RedisSettings__Enabled, RedisSettings__ConnectionString |
| Stripe test mode | Stripe__Enabled, Stripe__SecretKey, Stripe__WebhookSecret, Stripe__SuccessUrl, Stripe__CancelUrl |
| Cloudflare R2 | R2__Enabled, R2__ServiceUrl, R2__PublicBaseUrl, R2__BucketName, R2__AccessKeyId, R2__SecretAccessKey |

Keep secrets in runtime environment settings, outside source control. See the configuration classes for optional non-secret limits and timeouts. Never put backend credentials in the frontend's public VITE variables.

## Build and test

```powershell
dotnet build RiuTek_API.slnx -c Release
dotnet test RiuTek.Application.Test/RiuTek.Application.Test.csproj -c Release
dotnet test RiuTek.API.IntegrationTest/RiuTek.API.IntegrationTest.csproj -c Release
```

The integration tests start an isolated PostgreSQL/pgvector container and apply migrations. They do not use the production Supabase database. External services are replaced with test implementations where appropriate.

## Deployment

```powershell
docker build -f RiuTek.API/Dockerfile -t riutek-api:local .
```

The image runs the API as a non-root user on port 8080. Supply configuration at runtime; do not include secrets in the image. GET /health/live checks process liveness, not database or external-service readiness.

Pushes to master run the GitHub Actions workflow: build and tests, Docker build/push to ACR using the commit SHA, EF migrations to Supabase, deployment to Azure App Service, and HTTP smoke checks. The deployed application uses Supabase PostgreSQL, Upstash Redis and Cloudflare R2. Credentials are supplied through GitHub/Azure settings.

The frontend is deployed separately on Vercel and proxies API requests through /api/v1. See the frontend repository for browser setup and deployment details.

## Roadmap and scope

This is a personal portfolio project. Stripe runs in test mode; the deployed site is intended for demonstrating the workflows, not claiming a live retail operation.

Future work:
- Customer-facing posts, comments and reviews screens, using the existing APIs.
- AI-assisted build recommendations and semantic product search.
- VNPay integration and broader return/refund workflows.
- Laptop and prebuilt-PC catalog support as independent stocked products, without component-level stock or compatibility rules for those products.
