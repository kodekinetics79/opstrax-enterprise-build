export type AuthenticatedUser = {
  id: number;
  companyId: number;
  companyCode: string;
  companyName: string;
  email: string;
  fullName: string;
  roleId: number | null;
  roleName: string;
  permissions: string[];
};

export type AuthSessionPayload = {
  token: string;
  csrfToken: string;
  user: {
    id: number;
    email: string;
    name: string;
    fullName: string;
    companyId: number;
    companyCode: string;
  };
  role: string;
  company: {
    id: number;
    companyId: number;
    code: string;
    name: string;
  };
  permissions: string[];
};
