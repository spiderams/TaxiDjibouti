import axios from "axios";

export type UserRole = "Client" | "Driver" | "Admin";
export type RideAction = "arrived" | "start" | "complete" | "cancel";

export interface AuthResponse {
  token: string;
  userId: number;
  fullName: string;
  role: UserRole;
}

export interface UserSummary {
  id: number;
  fullName: string;
  phoneNumber: string;
  role: UserRole;
  createdAt?: string;
}

export interface DriverProfile {
  id: number;
  userId: number;
  user?: UserSummary | null;
  licenseNumber: string;
  vehiclePlate: string;
  vehicleType: string;
  isAvailable: boolean;
  averageRating: number;
  createdAt: string;
}

export interface Ride {
  id: number;
  clientId: number;
  client?: UserSummary | null;
  driverId?: number | null;
  driver?: DriverProfile | null;
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
      userId: number;
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
        "/api/Drivers/set-available",
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
