# Local Development Setup

<cite>
**Referenced Files in This Document**
- [README.md](file://README.md)
- [docker-compose.yml](file://docker-compose.yml)
- [start-local.sh](file://start-local.sh)
- [stop-local.sh](file://stop-local.sh)
- [reset-local.sh](file://reset-local.sh)
- [package.json](file://package.json)
- [db/init/001_schema.sql](file://db/init/001_schema.sql)
- [db/init/002_seed.sql](file://db/init/002_seed.sql)
- [backend-dotnet/Program.cs](file://backend-dotnet/Program.cs)
- [frontend/package.json](file://frontend/package.json)
- [frontend/Dockerfile](file://frontend/Dockerfile)
- [backend-dotnet/Dockerfile](file://backend-dotnet/Dockerfile)
- [services/node-events/Dockerfile](file://services/node-events/Dockerfile)
- [frontend/vite.config.ts](file://frontend/vite.config.ts)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Prerequisites](#prerequisites)
3. [Environment Setup](#environment-setup)
4. [Database Initialization](#database-initialization)
5. [Service Startup Procedures](#service-startup-procedures)
6. [Script-Based Deployment Workflow](#script-based-deployment-workflow)
7. [IDE Configuration and Debugging](#ide-configuration-and-debugging)
8. [Hot-Reload Development](#hot-reload-development)
9. [Contributing Guidelines](#contributing-guidelines)
10. [Testing Across Environments](#testing-across-environments)
11. [Troubleshooting Guide](#troubleshooting-guide)
12. [Conclusion](#conclusion)

## Introduction
This guide provides comprehensive instructions for setting up a local development environment for OpsTrax. It covers prerequisites, environment configuration, database initialization, service startup, script-based deployment workflows, IDE setup, debugging, hot-reload development, contribution guidelines, cross-environment testing, and troubleshooting common issues.

## Prerequisites
Before starting, ensure the following software is installed on your local machine:
- Docker Desktop: Required for containerized services and orchestration.
- Node.js: Version 22 or higher is required by the project.
- .NET SDK: Version 8.0 is required for building and running the backend API.

Notes:
- The repository enforces Node.js version 22 or higher globally.
- The backend API requires .NET 8.0 SDK for local builds and development.
- Docker Desktop is required for the orchestrated local environment.

**Section sources**
- [package.json:1-7](file://package.json#L1-L7)
- [backend-dotnet/Dockerfile:1-13](file://backend-dotnet/Dockerfile#L1-L13)
- [services/node-events/Dockerfile:1-8](file://services/node-events/Dockerfile#L1-L8)

## Environment Setup
Configure your local environment by following these steps:

1. Clone the repository and navigate to the project root.
2. Prepare the environment configuration:
   - Copy the example environment file to `.env` if it does not exist.
   - The shell scripts handle copying the example file automatically during startup.
3. Verify Docker Desktop is running and has sufficient resources allocated.

Ports and services:
- Frontend: Port 10000 (exposed by the frontend service).
- API: Port 8088 (mapped from internal port 8080).
- Node Events: Port 8090 (WebSocket service).
- Database: Internal MySQL 8.4 (not exposed externally).

**Section sources**
- [README.md:17-20](file://README.md#L17-L20)
- [docker-compose.yml:13-14](file://docker-compose.yml#L13-L14)
- [docker-compose.yml:29-30](file://docker-compose.yml#L29-L30)
- [docker-compose.yml:42-43](file://docker-compose.yml#L42-L43)

## Database Initialization
OpsTrax initializes the database using SQL scripts located under the db/init directory. These scripts create the schema and seed realistic demo data.

Initialization steps:
1. The database schema is created by running the schema script.
2. Demo data is inserted by running the seed script.
3. The backend API performs automatic schema migrations on startup.

Key files:
- Schema creation script: [db/init/001_schema.sql](file://db/init/001_schema.sql)
- Seed data script: [db/init/002_seed.sql](file://db/init/002_seed.sql)

Verification:
- After startup, the API health endpoint confirms database connectivity.
- The deep health endpoint aggregates database, service, and configuration checks.

**Section sources**
- [db/init/001_schema.sql:1-263](file://db/init/001_schema.sql#L1-L263)
- [db/init/002_seed.sql:1-70](file://db/init/002_seed.sql#L1-L70)
- [backend-dotnet/Program.cs:65-90](file://backend-dotnet/Program.cs#L65-L90)
- [backend-dotnet/Program.cs:296-378](file://backend-dotnet/Program.cs#L296-L378)

## Service Startup Procedures
There are two primary ways to start the local environment:

Option A: Using Docker Compose (recommended)
- Run: `docker compose up --build`
- This builds and starts all services defined in the compose file.

Option B: Using Shell Scripts
- Start all services: [start-local.sh](file://start-local.sh)
- Stop all services: [stop-local.sh](file://stop-local.sh)
- Reset and rebuild everything: [reset-local.sh](file://reset-local.sh)

Post-start verification:
- Frontend: http://localhost:10000
- API Swagger: http://localhost:8088/swagger
- Node Events health: http://localhost:8090/health

**Section sources**
- [README.md:69-81](file://README.md#L69-L81)
- [start-local.sh:1-15](file://start-local.sh#L1-L15)
- [stop-local.sh:1-4](file://stop-local.sh#L1-L4)
- [reset-local.sh:1-11](file://reset-local.sh#L1-L11)

## Script-Based Deployment Workflow
The repository provides three shell scripts to manage the local environment lifecycle:

- start-local.sh
  - Copies .env.example to .env if needed.
  - Installs frontend dependencies and builds the frontend.
  - Stops existing containers and removes orphaned instances.
  - Starts all services with Docker Compose.
  - Prints URLs for quick access.

- stop-local.sh
  - Stops and removes containers managed by Docker Compose.

- reset-local.sh
  - Installs frontend dependencies and rebuilds.
  - Performs a full teardown with volume removal.
  - Rebuilds and starts all services.

Best practices:
- Use start-local.sh for daily development.
- Use reset-local.sh when you suspect persistent state corruption.
- Use stop-local.sh to cleanly halt the environment.

**Section sources**
- [start-local.sh:1-15](file://start-local.sh#L1-L15)
- [stop-local.sh:1-4](file://stop-local.sh#L1-L4)
- [reset-local.sh:1-11](file://reset-local.sh#L1-L11)

## IDE Configuration and Debugging
Recommended IDE setup for efficient local development:

Frontend (React/Vite):
- Use an IDE with TypeScript and Vite support.
- Configure the frontend dev server to run on port 10000 with strict port enforcement.
- Enable ESLint integration for code quality.

Backend (.NET 8):
- Open the backend-dotnet project in your preferred IDE.
- Ensure .NET 8 SDK is selected as the target framework.
- Set breakpoints in controller actions and middleware for debugging.

Node Events (WebSocket):
- Open the node-events service in your IDE.
- Use Node.js debugging capabilities to attach to the WebSocket server process.

Debugging tips:
- Use browser developer tools for frontend debugging.
- Use IDE debuggers for backend and Node.js services.
- Leverage API Swagger documentation for endpoint testing.

**Section sources**
- [frontend/package.json:9-14](file://frontend/package.json#L9-L14)
- [frontend/vite.config.ts:1-13](file://frontend/vite.config.ts#L1-L13)
- [backend-dotnet/Dockerfile:1-13](file://backend-dotnet/Dockerfile#L1-L13)
- [services/node-events/Dockerfile:1-8](file://services/node-events/Dockerfile#L1-L8)

## Hot-Reload Development
Enable hot-reload for rapid iteration:

Frontend:
- Run the Vite dev server with host binding and strict port enforcement.
- Changes to React components and styles trigger immediate reloads.

Backend:
- Use the .NET CLI to run the API in development mode with hot reload enabled.
- Changes to C# code recompile automatically.

Node Events:
- Use nodemon or native Node.js watch mode to restart the WebSocket server on changes.

Containerized development:
- For containerized workflows, rely on mounted volumes and live reload features.
- Keep the frontend dev server bound to 0.0.0.0 for external access.

**Section sources**
- [frontend/package.json:10](file://frontend/package.json#L10)
- [frontend/vite.config.ts:10](file://frontend/vite.config.ts#L10)
- [backend-dotnet/Dockerfile:1-13](file://backend-dotnet/Dockerfile#L1-L13)
- [services/node-events/Dockerfile:1-8](file://services/node-events/Dockerfile#L1-L8)

## Contributing Guidelines
Follow these guidelines when contributing to the project:

Repository structure awareness:
- The repository contains both a backend-dotnet and a backend directory. Ensure contributions align with the intended architecture and service boundaries.

Branching and commits:
- Create feature branches from the latest main branch.
- Write clear commit messages describing changes and their impact.

Code standards:
- Follow existing code style and linting rules enforced by ESLint and TypeScript configurations.
- Ensure backend C# code adheres to .NET 8 conventions and patterns.

Testing:
- Add unit tests for new backend services and frontend components.
- Validate API changes using Swagger documentation endpoints.

Documentation updates:
- Update README and inline comments when changing environment setup or service behavior.

**Section sources**
- [README.md:1-166](file://README.md#L1-L166)
- [frontend/package.json:34-40](file://frontend/package.json#L34-L40)
- [backend-dotnet/Program.cs:10-14](file://backend-dotnet/Program.cs#L10-L14)

## Testing Across Environments
Validate your changes across different environments:

Local environment:
- Use the provided scripts to start, stop, and reset the environment.
- Test frontend, API, and WebSocket services independently.

Integration testing:
- Verify CORS configuration allows requests from the frontend origin.
- Confirm database migrations and seed data are applied correctly.

Cross-service communication:
- Test API endpoints through Swagger.
- Validate WebSocket connections for real-time features.

Production parity:
- Use Docker Compose to mirror production-like conditions.
- Validate port mappings and service dependencies.

**Section sources**
- [docker-compose.yml:25-28](file://docker-compose.yml#L25-L28)
- [docker-compose.yml:38-41](file://docker-compose.yml#L38-L41)
- [backend-dotnet/Program.cs:55-63](file://backend-dotnet/Program.cs#L55-L63)

## Troubleshooting Guide
Common local development issues and resolutions:

Port conflicts:
- Frontend port 10000, API port 8088, and Node Events port 8090 must be free.
- If conflicts occur, stop the conflicting applications or adjust the compose file mappings.

Docker-related issues:
- Ensure Docker Desktop is running and has sufficient memory/CPU allocation.
- Clear stopped containers and networks if startup fails.
- Rebuild images after making changes to Dockerfiles.

Environment variables:
- Copy .env.example to .env before first run.
- Verify CORS and connection string settings match the compose network.

Database problems:
- Confirm the database schema and seed scripts executed successfully.
- Use the API health endpoints to diagnose connectivity issues.

Frontend build errors:
- Ensure Node.js version meets the project requirement (>= 22).
- Reinstall dependencies and rebuild the frontend.

Backend runtime errors:
- Check logs for schema migration failures or missing dependencies.
- Verify .NET 8 SDK availability and correct project configuration.

Node Events issues:
- Confirm the WebSocket server binds to the expected port.
- Validate CORS settings for frontend origin.

**Section sources**
- [README.md:17-20](file://README.md#L17-L20)
- [start-local.sh:3](file://start-local.sh#L3)
- [package.json:3-5](file://package.json#L3-L5)
- [backend-dotnet/Program.cs:296-378](file://backend-dotnet/Program.cs#L296-L378)
- [docker-compose.yml:13-14](file://docker-compose.yml#L13-L14)
- [docker-compose.yml:29-30](file://docker-compose.yml#L29-L30)
- [docker-compose.yml:42-43](file://docker-compose.yml#L42-L43)

## Conclusion
You now have a complete understanding of how to set up and operate the OpsTrax local development environment. By following the prerequisites, environment configuration, database initialization, and script-based workflows outlined here, you can efficiently develop, test, and debug across all services. Use the troubleshooting section to resolve common issues quickly and adhere to the contributing guidelines for smooth collaboration.