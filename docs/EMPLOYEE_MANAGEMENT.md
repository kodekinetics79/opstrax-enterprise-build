# Employee Management Module

The Employees module now supports the core enterprise lifecycle:

- Create employee draft
- Capture bilingual personal information, emergency contacts, employment grade/cost center, and GCC identity fields
- Capture employment details, manager, department, designation, branch, payroll profile, shift policy, and leave policy
- Upload and download required employee documents through tenant-scoped local storage
- Submit draft for HR approval
- HR approval generates employee ID, creates an employee user account, activates the employee, and writes history
- Sensitive field updates create approval requests instead of changing the profile directly
- Transfer requests move through current manager, new manager, and HR approval before updating the employee profile
- Reports expose headcount, active employees, new joiners, exits, probation, department/branch/nationality/gender mix, expiry risk, and incomplete profiles
- Employee AI endpoint supports natural language style queries for expiry risk, missing bank details, probation endings, and incomplete onboarding
- In-app notifications are created for draft submission, document upload, activation, change approval, and transfer milestones
- Localized contract, offer, and sponsorship templates can be generated in English or Arabic
- Gregorian dates can be converted to Hijri dates through the Um Al-Qura calendar service

Remaining production integrations:

- Virus scanning for uploaded documents
- Optional S3/Azure Blob storage provider instead of local disk storage
- Email/SMS/WhatsApp notification delivery channels
- Legal review of country-specific template wording before live deployment
