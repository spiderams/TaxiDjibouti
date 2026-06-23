import axios from "axios";

export type UserRole = "Client" | "Driver" | "Admin";
export type RideAction = "arrived" | "start" | "complete" | "cancel";

export interface AuthUser {
  id: string;
  fullName: string;
  phoneNumber: string;
  roles: UserRole[];
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  tokenType: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}

export function primaryRole(user: AuthUser): UserRole {
  if (user.roles.includes("Admin")) return "Admin";
  if (user.roles.includes("Driver")) return "Driver";
  return "Client";
}

export interface UserSummary {
  id: string;
  fullName: string;
  phoneNumber: string;
  roles: UserRole[];
}

export interface DriverProfile {
  id: number;
  userId: string;
  licenseNumber: string;
  vehiclePlate: string;
  vehicleType: string;
  isAvailable: boolean;
  averageRating: number;
}

export interface Ride {
  id: number;
  clientId: string;
  driverId?: number | null;

  pickupAddress: string;
  destinationAddress: string;

  pickupZone: string;
  destinationZone: string;

  pickupLatitude?: number | null;
  pickupLongitude?: number | null;
  destinationLatitude?: number | null;
  destinationLongitude?: number | null;

  estimatedPrice: number;
  status: string;

  createdAt: string;
  acceptedAt?: string | null;
  completedAt?: string | null;

  /**
   * Champs optionnels utiles pour Wave Dispatch.
   * Ils ne cassent pas le frontend si le backend ne les renvoie pas encore.
   */
  offeredDriverIds?: number[];
  triedDriverIds?: number[];
  offerExpiresAt?: string | null;
}

export interface AdminStats {
  users: number;
  drivers: number;
  rides: number;
  reports: number;
}

export interface CreateDriverPayload {
  licenseNumber: string;
  vehiclePlate: string;
  vehicleType: string;
}

export interface AdminCreateDriverPayload extends CreateDriverPayload {
  userId: string;
}

export interface RequestRidePayload {
  pickupAddress: string;
  destinationAddress: string;
  pickupZone: string;
  destinationZone: string;
  pickupLatitude?: number | null;
  pickupLongitude?: number | null;
  destinationLatitude?: number | null;
  destinationLongitude?: number | null;
}

export interface RegisterPayload {
  fullName: string;
  phoneNumber: string;
  password: string;
  role: UserRole;
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export const http = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const data = error.response?.data;

    const message =
      typeof data === "string"
        ? data
        : data?.detail ||
          data?.title ||
          data?.message ||
          error.message ||
          "Action impossible";

    return Promise.reject(new Error(message));
  },
);

function authHeader(token: string) {
  return {
    headers: {
      Authorization: `Bearer ${token}`,
    },
  };
}

export const api = {
  login: async (phoneNumber: string, password: string) =>
    (
      await http.post<AuthResponse>("/api/Auth/login", {
        phoneNumber,
        password,
      })
    ).data,

  register: async (payload: RegisterPayload) =>
    (await http.post<AuthResponse>("/api/Auth/register", payload)).data,

  refreshToken: async (refreshToken: string) =>
    (await http.post<AuthResponse>("/api/Auth/refresh", { refreshToken })).data,

  createDriver: async (payload: CreateDriverPayload, token: string) =>
    (await http.post<DriverProfile>("/api/Drivers", payload, authHeader(token)))
      .data,

  /**
   * À utiliser seulement si ton backend a un endpoint Admin dédié.
   * Si cet endpoint n'existe pas encore, ne l'appelle pas dans main.tsx.
   */
  adminCreateDriver: async (payload: AdminCreateDriverPayload, token: string) =>
    (
      await http.post<DriverProfile>(
        "/api/Admin/drivers",
        payload,
        authHeader(token),
      )
    ).data,

  setAvailability: async (isAvailable: boolean, token: string) =>
    (
      await http.post<DriverProfile>(
        "/api/Drivers/set-availability",
        { isAvailable },
        authHeader(token),
      )
    ).data,

  requestRide: async (payload: RequestRidePayload, token: string) =>
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

  /**
   * Alias utile pour Wave Dispatch.
   * Tu peux utiliser acceptOffer dans l'UI chauffeur au lieu de acceptRide.
   */
  acceptOffer: async (rideId: number, token: string) =>
    (
      await http.post<Ride>(
        `/api/Rides/${rideId}/accept`,
        undefined,
        authHeader(token),
      )
    ).data,

  /**
   * À utiliser si ton backend expose l'action de refus d'offre.
   */
  declineOffer: async (rideId: number, token: string) =>
    (
      await http.post<void>(
        `/api/Rides/${rideId}/decline`,
        undefined,
        authHeader(token),
      )
    ).data,

  updateRideStatus: async (
    rideId: number,
    action: RideAction,
    token: string,
  ) =>
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