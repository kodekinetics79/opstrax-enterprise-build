#!/usr/bin/env bash
set -e
cp -n .env.example .env 2>/dev/null || true
(
  cd frontend
  npm install
  npm run build
)
docker compose down --remove-orphans
docker rm -f opstrax-frontend opstrax-dotnet-api opstrax-node-events opstrax-mysql 2>/dev/null || true
docker compose up --build
echo "OpsTrax frontend: http://localhost:10000"
echo "OpsTrax API Swagger: http://localhost:8088/swagger"
echo "OpsTrax Node Events: http://localhost:8090/health"
