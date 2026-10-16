# RiuTek API

[![CI & Publish to ACR](https://github.com/RiuTek/RiuTek_API/actions/workflows/ci-acr.yml/badge.svg)](https://github.com/RiuTek/RiuTek_API/actions/workflows/ci-acr.yml)

Backend API for a PC-component e-commerce and PC-building platform. RiuTek is designed to help users browse hardware, validate component compatibility, manage a persistent cart, and complete an order even when they are not familiar with building a PC.

This is an actively developed personal portfolio project. It focuses on backend architecture, transactional e-commerce workflows, automated testing, and cloud deployment. The long-term goal is to add an AI-assisted PC advisor on top of deterministic compatibility rules and semantic product search.

## What the project solves

Buying PC parts requires more than adding products to a cart: sockets, memory generations, form factors, power requirements, cooler support, and physical dimensions must be compatible. RiuTek combines a conventional product catalog and checkout flow with structured hardware specifications and a compatibility engine so that users can build a valid configuration with clearer guidance.

## Current capabilities

- JWT authentication with refresh-token rotation, BCrypt password hashing, HttpOnly refresh cookies, and role-based authorization for Customer, Staff, and Admin.
- Customer profile and address management with ownership protection and deterministic default-address rules.
- Product and category management for nine component types: CPU, motherboard, GPU, RAM, storage, PSU, case, cooler, and accessory.
- Polymorphic JSON hardware specifications, filtering, search, sorting, and deterministic pagination.
- PC Builder aggregate and rule-based hardware compatibility checks.
- Authenticated persistent cart with version-based conflict detection and automatic cleanup of inactive carts after 30 days.
- Checkout quotation, idempotent order creation, stock reservation, order history, Stripe Checkout sandbox integration, signed webhook processing, and COD checkout support.
- Posts and reusable comments for products and posts.
- Redis-backed caching with a database fallback when Redis is unavailable.
- Cloudflare R2 product-media uploads through presigned URLs, with validation and ownership rules verified in the deployed environment.
- Versioned Excel template generation, dry-run validation, and transactional create-only bulk import for all nine component types.
- PostgreSQL integration tests with Testcontainers and automated container deployment through GitHub Actions.

## Architecture

RiuTek is a modular monolith using Clean Architecture. The Application layer is organized with Vertical Slices and CQRS.

```text
RiuTek.API
    HTTP contracts, controllers, authentication middleware
        |
        v
RiuTek.Application
    Commands, queries, handlers, validators, DTOs, interfaces
        |
        v
RiuTek.Core
    Entities, enums, domain specifications, business abstractions

RiuTek.Infrastructure
    EF Core, PostgreSQL, Redis, JWT, Stripe, Cloudflare R2 adapters
        |
        +---- implements Application/Core interfaces
```

Key design choices:

- `Result<T>` and typed errors are used for expected business failures instead of exception-driven control flow.
- FluentValidation runs through the MediatR pipeline.
- DTO mapping is explicit rather than hidden behind AutoMapper.
- Authorization and ownership are enforced again in handlers, not only through controller attributes.
- Product price, availability, stock, and cart version are revalidated during checkout.
- External integrations are represented by Application interfaces and implemented in Infrastructure.
- Optional services such as Redis, Stripe, and R2 can be disabled without breaking the core API.

## Tech stack

| Area | Technologies |
|---|---|
| Runtime | .NET 10, ASP.NET Core Web API, C# |
| Architecture | Clean Architecture, Vertical Slice Architecture, CQRS, MediatR |
| Validation | FluentValidation |
| Data | PostgreSQL, EF Core 10, Npgsql, pgvector |
| Authentication | JWT Bearer, refresh-token rotation, BCrypt |
| Caching | Redis / Upstash-compatible Redis |
| Payments | Stripe Checkout and webhooks, COD |
| Media | Cloudflare R2 through the S3-compatible API |
| Imports | ClosedXML, versioned `.xlsx` contracts, dry-run validation, transactional batch creation |
| Testing | xUnit, FluentAssertions, ASP.NET Core integration testing, Testcontainers |
| Delivery | Docker, GitHub Actions, Azure Container Registry, Azure Web App, Supabase |

## Repository structure

```text
RiuTek.Core/                 Domain model and business abstractions
RiuTek.Application/          CQRS slices, validation, DTOs and ports
RiuTek.Infrastructure/       Database and external-service adapters
RiuTek.API/                  HTTP API and composition root
RiuTek.Application.Test/     Unit and contract tests
RiuTek.API.IntegrationTest/  HTTP/PostgreSQL integration tests
.github/workflows/           CI/CD workflow
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL with the `vector` extension, or Docker Desktop for a local PostgreSQL/pgvector container
- Docker Desktop when running the integration-test project

Redis, Stripe, and R2 are optional for local development and should remain disabled until their settings are configured.

### Required configuration

Secrets must be supplied at runtime and must not be committed to any `appsettings*.json`, `.env`, workflow, or source file.

| Environment variable | Required | Purpose |
|---|---:|---|
| `ConnectionStrings__DefaultConnection` | Yes | PostgreSQL connection string |
| `JwtSettings__SecretKey` | Yes | Random JWT signing key, at least 32 UTF-8 bytes |
| `RedisSettings__Enabled` | No | Enables Redis caching |
| `RedisSettings__ConnectionString` | When Redis is enabled | Redis connection string |
| `Stripe__Enabled` | No | Enables Stripe sandbox checkout |
| `Stripe__SecretKey` | When Stripe is enabled | Stripe test secret key |
| `Stripe__WebhookSecret` | When Stripe is enabled | Stripe endpoint signing secret |
| `R2__Enabled` | No | Enables Cloudflare R2 product media |
| `R2__ServiceUrl` and `R2__PublicBaseUrl` | When R2 is enabled | S3 endpoint and public media base URL |
| `R2__AccessKeyId`, `R2__SecretAccessKey`, `R2__BucketName` | When R2 is enabled | Bucket-scoped R2 credentials and bucket name |

Example for a temporary PowerShell session:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = Read-Host "PostgreSQL connection string" -MaskInput
$env:JwtSettings__SecretKey = Read-Host "JWT signing key" -MaskInput

dotnet restore RiuTek_API.slnx
dotnet ef database update --project RiuTek.Infrastructure --startup-project RiuTek.API
dotnet run --project RiuTek.API --no-launch-profile
```

`Read-Host -MaskInput` requires PowerShell 7.1 or newer. Environment variables set this way live only in the current terminal session and are inherited by its child processes.

When running in Development, the OpenAPI document is available at:

```text
http://localhost:5000/openapi/v1.json
```

Use the actual host and port printed by `dotnet run`; the launch profile commonly uses `http://localhost:5251`.

## Build and test

Build the complete solution:

```powershell
dotnet build RiuTek_API.slnx -c Release
```

Run fast Application/unit/contract tests without Docker:

```powershell
dotnet test RiuTek.Application.Test/RiuTek.Application.Test.csproj -c Release
```

Run PostgreSQL HTTP integration tests with Docker Desktop running:

```powershell
dotnet test RiuTek.API.IntegrationTest/RiuTek.API.IntegrationTest.csproj -c Release
```

Run the complete suite:

```powershell
dotnet test RiuTek_API.slnx -c Release
```

The integration project starts an isolated `pgvector/pgvector:pg17` container, applies EF Core migrations, executes the tests, and disposes the database afterward. It does not connect to the production Supabase database.

## Docker

Build the production image:

```powershell
docker build -f RiuTek.API/Dockerfile -t riutek-api:local .
```

The final Linux image:

- uses the official .NET 10 ASP.NET runtime;
- runs as the non-root `app` user;
- listens on port `8080`;
- contains no production credentials;
- exposes `GET /health/live` as a process liveness endpoint.

Runtime secrets must be injected through the deployment environment. The liveness endpoint intentionally does not prove database, Redis, Stripe, or R2 connectivity.

## CI/CD and deployment

Every push to `master` runs the GitHub Actions pipeline:

1. restore, Release build, and all automated tests;
2. build an immutable Docker image tagged with the commit SHA;
3. push the image to Azure Container Registry;
4. apply EF Core migrations to Supabase PostgreSQL;
5. deploy the exact image to Azure Web App;
6. smoke-test liveness, products, categories, and posts.

Credentials and connection strings are stored in GitHub/Azure environment settings, not in the repository. Production currently uses Azure Web App, Supabase PostgreSQL, and Upstash Redis.

## Development status and roadmap

Completed or operationally verified:

- authentication and profile/address management;
- catalog, compatibility rules, authenticated cart, checkout, order history;
- Stripe sandbox success, signed webhook, and duplicate-event handling;
- Cloudflare R2 product-media upload and deletion lifecycle;
- versioned Excel template, dry-run validation, and all-or-nothing product import for all nine component types;
- Docker image, PostgreSQL integration testing, CI/CD, Azure/Supabase/Redis deployment.

In progress or planned:

- product import history and richer administrative audit reporting;
- complete administrative order lifecycle and COD delivery/payment confirmation;
- product reviews and inventory-management workflows;
- AI-assisted PC recommendations using embeddings and pgvector;
- React + Vite customer and administration frontend;
- optional VNPay integration.

## Project scope

RiuTek is a solo learning and portfolio project, not a claim of production-scale operation. Some patterns intentionally model real backend concerns—idempotency, concurrency, ownership, failure isolation, migrations, and CI/CD—so they can be studied and tested in one codebase. Features are implemented incrementally and marked as complete only after automated and, where relevant, deployed-environment verification.
