# KynexOne

**One Platform for Every Workforce Operation**

AI-Powered HRM and Workforce Operating System.

## Stack
- Frontend: React + Vite + TypeScript + Tailwind CSS
- Enterprise API: .NET 8 Web API + EF Core
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

## Modules
- Command Center (Dashboard)
- Workforce (Employees, Departments, Organization)
- Time & Leave (Attendance, Leave, Overtime, Shifts)
- Payroll (Runs, Payslips, Loans & Advances)
- Talent (Recruitment, Onboarding)
- Performance (Reviews, KPIs)
- Operations (Approvals, HR Request Center, Documents, Compliance)
- Reports & Analytics
- Administration (Users, Roles, Permissions, Tenant Admin, Setup)
- KynexOne AI (advisory assistant)

> **Note:** The internal .NET solution and namespaces retain the `Zayra.Api`
> project name for build stability; this is not user-facing. All UI, copy, and
> product surfaces are branded **KynexOne**.
