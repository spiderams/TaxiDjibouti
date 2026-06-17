import * as signalR from "@microsoft/signalr";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export interface DriverLocationPayload {
  rideId: number;
  latitude: number;
  longitude: number;
  heading?: number | null;
  speed?: number | null;
  driverId?: number;
  sentAt?: string;
}

export function createRideHubConnection(token: string) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/ride`, { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();
}

export async function joinClientLocationGroup(
  connection: signalR.HubConnection,
  clientId: number,
) {
  await connection.invoke("JoinClientGroup", clientId.toString());
}

export async function joinRideLocationGroup(
  connection: signalR.HubConnection,
  rideId: number,
) {
  await connection.invoke("JoinRideGroup", rideId);
}

export async function joinAdminLocationGroup(
  connection: signalR.HubConnection,
) {
  await connection.invoke("JoinAdminsGroup");
}

export async function sendDriverLocation(
  connection: signalR.HubConnection,
  location: DriverLocationPayload,
) {
  await connection.invoke("SendDriverLocation", location);
}
