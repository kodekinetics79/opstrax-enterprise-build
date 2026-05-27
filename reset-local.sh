#!/usr/bin/env bash
set -e
(
  cd frontend
  npm install
  npm run build
)
docker compose down -v --remove-orphans
docker rm -f opstrax-frontend opstrax-dotnet-api opstrax-node-events opstrax-mysql 2>/dev/null || true
docker compose up --build
