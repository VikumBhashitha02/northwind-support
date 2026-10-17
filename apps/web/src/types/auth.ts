export interface CurrentUser {
  id: number;
  fullName: string;
  email: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  user: CurrentUser;
}
