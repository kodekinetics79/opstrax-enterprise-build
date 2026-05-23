# Zayra AI Workforce

AI-powered GCC workforce operations platform.

## Stack
- Frontend: React + Vite
- Enterprise API: .NET 8 Web API
- AI/Automation Microservice: Node.js + Express
- Database: MySQL

## Run Frontend
cd frontend
npm install
npm run dev

## Run .NET API
cd backend-dotnet/Zayra.Api
dotnet restore
dotnet run
Swagger: http://localhost:5117/swagger or printed launch URL

## Run Node AI Service
cd backend-node
cp .env.example .env
npm install
npm run dev

## Database
Run database/schema.sql in MySQL.

## MVP Modules Included
- Dashboard prototype
- Employee master shell
- Attendance/time-in/time-out model
- Overtime model
- Requisition table
- Appraisal cycle table
- AI insights service shell
