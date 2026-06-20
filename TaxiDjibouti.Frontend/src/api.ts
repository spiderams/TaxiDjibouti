import axios from "axios";

export type UserRole = "Client" | "Driver" | "Admin";
export type RideAction = "arrived" | "start" | "complete" | "cancel";

/**
 * Informations sur l'utilisateur authentifié, telles que renvoyées par le backend
 * dans l'objet imbriqué `user` de la réponse d'authentification.
 */
export interface AuthUser {
  id: string;
  fullName: string;
  phoneNumber: string;
  roles: UserRole[];
}

/**
 * Réponse d'authentification renvoyée par /api/Auth/login et /api/Auth/register.
 * La forme reflète exactement le DTO backend (AuthResponse) : jeton d'accès,
 * jeton de rafraîchissement, et objet `user` imbriqué contenant les rôles.
 */
export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  tokenType: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}

/**
 * Détermine le rôle le plus privilégié d'un utilisateur authentifié,
 * utilisé pour décider de l'espace vers lequel rediriger (Admin > Driver > Client).
 */
export function primaryRole(user: AuthUser): UserRole {
  if (user.roles.includes("Admin")) return "Admin";
  if (user.roles.includes("Driver")) return "Driver";
  return "Client";
}

/**
 * Résumé d'utilisateur renvoyé par /api/admin/users.
 * Reflète le DTO backend UserSummary : id Identity (string GUID) et liste de rôles.
 */
export interface UserSummary {
  id: string;
  fullName: string;
  phoneNumber: string;
  roles: UserRole[];
}

/**
 * Profil chauffeur renvoyé par le backend (DriverDto).
 * `id` est l'identifiant métier (int), `userId` l'identifiant Identity (string GUID).
 */
export interface DriverProfile {
  id: number;
  userId: string;
  licenseNumber: string;
  vehiclePlate: string;
  vehicleType: string;
  isAvailable: boolean;
  averageRating: number;
}

/**
 * Course renvoyée par le backend (RideDto).
 * `id`/`driverId` sont des identifiants métier (int), `clientId` est un id Identity (string GUID).
 * `status` est l'enum RideStatus sérialisé en string ("Pending", "Accepted", ...).
 */
export interface Ride {
  id: number;
  clientId: string;
  driverId?: number | null;
  pickupAddress: string;
  destinationAddress: string;
  pickupZone: string;
  pickupLatitude?: number | null;
  pickupLongitude?: number | null;
  destinationLatitude?: number | null;
  destinationLongitude?: number | null;
  destinationZone: string;
  estimatedPrice: number;
  status: string;
  createdAt: string;
  acceptedAt?: string | null;
  completedAt?: string | null;
}

export interface AdminStats {
  users: number;
  drivers: number;
  rides: number;
  reports: number;
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export const http = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const message =
      error.response?.data || error.message || "Action impossible";
    return Promise.reject(
      new Error(
        typeof message === "string" ? message : JSON.stringify(message),
      ),
    );
  },
);

function authHeader(token: string) {
  return { headers: { Authorization: `Bearer ${token}` } };
}

export const api = {
  login: async (phoneNumber: string, password: string) =>
    (
      await http.post<AuthResponse>("/api/Auth/login", {
        phoneNumber,
        password,
      })
    ).data,
  register: async (payload: {
    fullName: string;
    phoneNumber: string;
    password: string;
    role: UserRole;
  }) => (await http.post<AuthResponse>("/api/Auth/register", payload)).data,
  createDriver: async (
    payload: {
      licenseNumber: string;
      vehiclePlate: string;
      vehicleType: string;
    },
    token: string,
  ) =>
    (await http.post<DriverProfile>("/api/Drivers", payload, authHeader(token)))
      .data,
  setAvailability: async (isAvailable: boolean, token: string) =>
    (
      await http.post<DriverProfile>(
        "/api/Drivers/set-availability",
        { isAvailable },
        authHeader(token),
      )
    ).data,
  requestRide: async (
    payload: {
      pickupAddress: string;
      destinationAddress: string;
      pickupZone: string;
      destinationZone: string;
      pickupLatitude?: number | null;
      pickupLongitude?: number | null;
      destinationLatitude?: number | null;
      destinationLongitude?: number | null;
    },
    token: string,
  ) =>
    (await http.post<Ride>("/api/Rides/request", payload, authHeader(token)))
      .data,
  myRides: async (token: string) =>
    (await http.get<Ride[]>("/api/Rides/my-rides", authHeader(token))).data,
  pendingRides: async (token: string) =>
    (await http.get<Ride[]>("/api/Rides/pending", authHeader(token))).data,
  acceptRide: async (rideId: number, token: string) =>
    (
      await http.post<Ride>(
        `/api/Rides/${rideId}/accept`,
        undefined,
        authHeader(token),
      )
    ).data,
  updateRideStatus: async (rideId: number, action: RideAction, token: string) =>
    (
      await http.post<Ride>(
        `/api/Rides/${rideId}/${action}`,
        undefined,
        authHeader(token),
      )
    ).data,
  adminStats: async (token: string) =>
    (await http.get<AdminStats>("/api/Admin/stats", authHeader(token))).data,
  adminUsers: async (token: string) =>
    (await http.get<UserSummary[]>("/api/Admin/users", authHeader(token))).data,
  adminDrivers: async (token: string) =>
    (await http.get<DriverProfile[]>("/api/Admin/drivers", authHeader(token)))
      .data,
  adminRides: async (token: string) =>
    (await http.get<Ride[]>("/api/Admin/rides", authHeader(token))).data,
};
