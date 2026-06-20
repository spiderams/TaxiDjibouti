import React, { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  BrowserRouter,
  Link,
  Navigate,
  Route,
  Routes,
  useNavigate,
} from "react-router-dom";
import L from "leaflet";
import {
  AdminStats,
  AuthResponse,
  DriverProfile,
  Ride,
  UserRole,
  UserSummary,
  api,
  primaryRole,
} from "./api";
import { MapPicker, type MapPoint } from "./components/MapPicker";
import {
  createRideHubConnection,
  joinAdminLocationGroup,
  joinClientLocationGroup,
  sendDriverLocation,
  type DriverLocationPayload,
} from "./realtime";
import "leaflet/dist/leaflet.css";
import "./styles.css";

const zones = [
  "Centre-ville",
  "Balbala",
  "Aéroport",
  "Héron",
  "PK12",
  "Arhiba",
];
const storageKey = "taxi-djibouti-auth";
const defaultDjiboutiDriverLocation: MapPoint = {
  latitude: 11.5721,
  longitude: 43.1456,
};
const djiboutiDriverFallbackRadiusKm = 70;
const djiboutiMapBounds = L.latLngBounds([11.35, 42.85], [11.85, 43.45]);

function App() {
  const [session, setSession] = useState<AuthResponse | null>(() => {
    const saved = localStorage.getItem(storageKey);
    return saved ? (JSON.parse(saved) as AuthResponse) : null;
  });

  const saveSession = (auth: AuthResponse) => {
    localStorage.setItem(storageKey, JSON.stringify(auth));
    setSession(auth);
  };

  const logout = () => {
    localStorage.removeItem(storageKey);
    setSession(null);
  };

  return (
    <BrowserRouter>
      <div className="min-h-screen bg-taxi-sand text-taxi-navy">
        <header className="mx-auto flex w-[min(1180px,calc(100%-32px))] items-center justify-between py-5">
          <Link to="/" className="flex items-center gap-3 text-xl font-black">
            <span className="rounded-2xl bg-taxi-yellow px-3 py-2">🚕</span>
            Taxi Djibouti
          </Link>
          <nav className="hidden items-center gap-2 md:flex">
            <NavLink to="/client">Client</NavLink>
            <NavLink to="/chauffeur">Chauffeur</NavLink>
            <NavLink to="/admin">Admin</NavLink>
          </nav>
          {session ? (
            <button className="btn-secondary" onClick={logout}>
              Déconnexion
            </button>
          ) : (
            <Link className="btn-primary" to="/login">
              Connexion
            </Link>
          )}
        </header>

        <main className="mx-auto w-[min(1180px,calc(100%-32px))] pb-16">
          <Routes>
            <Route path="/" element={<Home session={session} />} />
            <Route
              path="/login"
              element={<AuthPage onAuthenticated={saveSession} />}
            />
            <Route
              path="/client"
              element={
                <Protected session={session} role="Client">
                  <ClientSpace session={session!} />
                </Protected>
              }
            />
            <Route
              path="/chauffeur"
              element={
                <Protected session={session} role="Driver">
                  <DriverSpace session={session!} />
                </Protected>
              }
            />
            <Route
              path="/admin"
              element={
                <Protected session={session} role="Admin">
                  <AdminSpace session={session!} />
                </Protected>
              }
            />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}

function Home({ session }: { session: AuthResponse | null }) {
  const role = session ? primaryRole(session.user) : null;
  const dashboardLink =
    role === "Admin"
      ? "/admin"
      : role === "Driver"
        ? "/chauffeur"
        : "/client";

  return (
    <section className="grid gap-6 lg:grid-cols-[1fr_340px]">
      <div className="card bg-gradient-to-br from-taxi-navy to-taxi-blue p-10 text-white">
        <span className="badge bg-taxi-yellow text-taxi-navy">
          MVP Taxi Djibouti
        </span>
        <h1 className="mt-5 max-w-4xl text-5xl font-black leading-none tracking-[-0.06em] md:text-7xl">
          Trois espaces simples pour réserver, conduire et administrer.
        </h1>
        <p className="mt-5 max-w-2xl text-lg text-blue-100">
          React + Vite + Tailwind CSS avec Axios, React Router, SignalR prêt
          pour le temps réel et Leaflet/OpenStreetMap prévu pour la carte.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link className="btn-yellow" to={session ? dashboardLink : "/login"}>
            {session ? "Ouvrir mon espace" : "Commencer"}
          </Link>
          <a className="btn-light" href="#roadmap">
            Voir la roadmap
          </a>
        </div>
      </div>
      <div className="card flex flex-col justify-end bg-taxi-yellow p-8">
        <span className="text-6xl">🚕</span>
        <strong className="mt-6 text-2xl">Centre-ville → Balbala</strong>
        <span className="text-taxi-navy/70">Prix estimé dès 1 500 FDJ</span>
      </div>
      <div id="roadmap" className="grid gap-4 lg:col-span-2 md:grid-cols-3">
        <FeatureCard
          title="1. Espace Client"
          items={[
            "Se connecter",
            "Demander un taxi",
            "Choisir départ / destination",
            "Voir prix estimé et statut",
          ]}
        />
        <FeatureCard
          title="2. Espace Chauffeur"
          items={[
            "Se connecter",
            "Se mettre disponible",
            "Voir courses en attente",
            "Changer le statut",
          ]}
        />
        <FeatureCard
          title="3. Espace Admin"
          items={[
            "Voir utilisateurs",
            "Voir chauffeurs",
            "Voir courses",
            "Voir statistiques",
          ]}
        />
      </div>
    </section>
  );
}

function AuthPage({
  onAuthenticated,
}: {
  onAuthenticated: (auth: AuthResponse) => void;
}) {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [fullName, setFullName] = useState("Client Test");
  const [phoneNumber, setPhoneNumber] = useState("77000002");
  const [password, setPassword] = useState("123456");
  const [role, setRole] = useState<UserRole>("Client");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError("");
    setLoading(true);

    try {
      const auth =
        mode === "login"
          ? await api.login(phoneNumber, password)
          : await api.register({ fullName, phoneNumber, password, role });
      onAuthenticated(auth);
      const userRole = primaryRole(auth.user);
      navigate(
        userRole === "Admin"
          ? "/admin"
          : userRole === "Driver"
            ? "/chauffeur"
            : "/client",
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Action impossible");
    } finally {
      setLoading(false);
    }
  }

  return (
    <section className="mx-auto max-w-2xl card p-6">
      <div className="mb-5 grid grid-cols-2 rounded-full bg-taxi-sand p-1">
        <button
          className={mode === "login" ? "tab-active" : "tab"}
          onClick={() => setMode("login")}
        >
          Connexion
        </button>
        <button
          className={mode === "register" ? "tab-active" : "tab"}
          onClick={() => setMode("register")}
        >
          Inscription
        </button>
      </div>
      <form onSubmit={submit} className="grid gap-4">
        {mode === "register" && (
          <Field label="Nom complet" value={fullName} onChange={setFullName} />
        )}
        <Field
          label="Téléphone"
          value={phoneNumber}
          onChange={setPhoneNumber}
        />
        <Field
          label="Mot de passe"
          type="password"
          value={password}
          onChange={setPassword}
        />
        {mode === "register" && (
          <label className="field-label">
            Rôle
            <select
              className="field"
              value={role}
              onChange={(event) => setRole(event.target.value as UserRole)}
            >
              <option value="Client">Client</option>
              <option value="Driver">Chauffeur</option>
              <option value="Admin">Admin</option>
            </select>
          </label>
        )}
        {error && (
          <p className="rounded-2xl bg-red-100 p-3 font-bold text-red-700">
            {error}
          </p>
        )}
        <button className="btn-primary" disabled={loading}>
          {loading ? "Chargement..." : "Continuer"}
        </button>
      </form>
    </section>
  );
}

function ClientSpace({ session }: { session: AuthResponse }) {
  const [pickupAddress, setPickupAddress] = useState("Place Menelik");
  const [destinationAddress, setDestinationAddress] = useState("Balbala");
  const [pickupZone, setPickupZone] = useState("Centre-ville");
  const [destinationZone, setDestinationZone] = useState("Balbala");
  const [pickupPoint, setPickupPoint] = useState<MapPoint | null>(null);
  const [destinationPoint, setDestinationPoint] = useState<MapPoint | null>(
    null,
  );
  const [driverLocation, setDriverLocation] = useState<MapPoint | null>(null);
  const [rides, setRides] = useState<Ride[]>([]);
  const [message, setMessage] = useState("");
  const token = session.accessToken;

  const refresh = async () => setRides(await api.myRides(token));

  useEffect(() => {
    refresh().catch(showError(setMessage));
  }, [token]);

  useEffect(() => {
    const connection = createRideHubConnection(token);

    connection.on("driverLocationUpdated", (payload: DriverLocationPayload) => {
      setDriverLocation({
        latitude: payload.latitude,
        longitude: payload.longitude,
      });
    });

    connection
      .start()
      .then(() => joinClientLocationGroup(connection, session.user.id))
      .catch(showError(setMessage));

    return () => {
      connection.stop().catch(() => undefined);
    };
  }, [session.user.id, token]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    await run(
      setMessage,
      async () => {
        await api.requestRide(
          {
            pickupAddress,
            destinationAddress,
            pickupZone,
            destinationZone,
            pickupLatitude: pickupPoint?.latitude,
            pickupLongitude: pickupPoint?.longitude,
            destinationLatitude: destinationPoint?.latitude,
            destinationLongitude: destinationPoint?.longitude,
          },
          token,
        );
        await refresh();
      },
      "Course demandée avec succès.",
    );
  }

  return (
    <SpaceShell
      title="Espace Client"
      subtitle="Demander un taxi, choisir départ/destination et suivre le statut."
      icon="🙋🏽"
    >
      {message && <Notice>{message}</Notice>}
      <div className="grid gap-5 xl:grid-cols-[1.1fr_0.9fr]">
        <section className="card p-6">
          <h2 className="section-title">Carte départ / destination</h2>
          <MapPicker
            pickup={pickupPoint}
            destination={destinationPoint}
            driverLocation={driverLocation}
            onPickupChange={setPickupPoint}
            onDestinationChange={setDestinationPoint}
          />
          <p className="mt-3 text-sm text-slate-500">
            Le premier clic place le départ, le deuxième clic place la
            destination. Les coordonnées sont gardées côté frontend pour
            préparer la prochaine évolution backend.
          </p>
        </section>

        <section className="card p-6">
          <h2 className="section-title">Demander un taxi</h2>
          <form className="grid gap-4 md:grid-cols-2" onSubmit={submit}>
            <Field
              label="Adresse départ"
              value={pickupAddress}
              onChange={setPickupAddress}
            />
            <Field
              label="Adresse destination"
              value={destinationAddress}
              onChange={setDestinationAddress}
            />
            <ZoneSelect
              label="Zone départ"
              value={pickupZone}
              onChange={setPickupZone}
            />
            <ZoneSelect
              label="Zone destination"
              value={destinationZone}
              onChange={setDestinationZone}
            />
            <CoordinatePreview label="Coordonnées départ" point={pickupPoint} />
            <CoordinatePreview
              label="Coordonnées destination"
              point={destinationPoint}
            />
            <CoordinatePreview
              label="Position chauffeur"
              point={driverLocation}
            />
            <div className="rounded-3xl bg-taxi-sand p-4 font-black md:col-span-2">
              Prix estimé : {estimatePrice(pickupZone, destinationZone)} FDJ
            </div>
            <button className="btn-primary md:col-span-2">
              Demander la course
            </button>
          </form>
        </section>
      </div>
      <FuturePanel
        title="Prochaine évolution temps réel"
        items={[
          "Envoyer la position chauffeur via navigator.geolocation.watchPosition",
          "Diffuser la position toutes les 5 secondes avec SignalR",
          "Déplacer un marker chauffeur sur cette carte côté client",
        ]}
      />
      <RideList rides={rides} title="Mes courses" />
    </SpaceShell>
  );
}

function DriverSpace({ session }: { session: AuthResponse }) {
  const [pendingRides, setPendingRides] = useState<Ride[]>([]);
  const [myRides, setMyRides] = useState<Ride[]>([]);
  const [message, setMessage] = useState("");
  const [trackingRideId, setTrackingRideId] = useState<number | null>(null);
  const [licenseNumber, setLicenseNumber] = useState("LIC-001");
  const [vehiclePlate, setVehiclePlate] = useState("DJ-1234");
  const [vehicleType, setVehicleType] = useState("Taxi");
  const token = session.accessToken;
  const locationConnectionRef = useRef<ReturnType<
    typeof createRideHubConnection
  > | null>(null);
  const locationWatchIdRef = useRef<number | null>(null);
  const locationStopTimerRef = useRef<number | null>(null);

  const refresh = async () => {
    setPendingRides(await api.pendingRides(token));
    setMyRides(await api.myRides(token));
  };

  useEffect(() => {
    refresh().catch(showError(setMessage));
  }, [token]);

  const action = (callback: () => Promise<unknown>, success: string) =>
    run(
      setMessage,
      async () => {
        await callback();
        await refresh();
      },
      success,
    );

  const stopLocationSharing = async (showStoppedMessage = true) => {
    if (locationWatchIdRef.current !== null) {
      navigator.geolocation.clearWatch(locationWatchIdRef.current);
      locationWatchIdRef.current = null;
    }

    if (locationStopTimerRef.current !== null) {
      window.clearTimeout(locationStopTimerRef.current);
      locationStopTimerRef.current = null;
    }

    if (locationConnectionRef.current) {
      await locationConnectionRef.current.stop().catch(() => undefined);
      locationConnectionRef.current = null;
    }

    setTrackingRideId(null);
    if (showStoppedMessage) {
      setMessage("Partage de position chauffeur arrêté.");
    }
  };

  useEffect(
    () => () => {
      void stopLocationSharing(false);
    },
    [],
  );

  const startLocationSharing = async (rideId: number) => {
    if (!navigator.geolocation) {
      setMessage(
        "La géolocalisation navigateur n’est pas disponible : position test Djibouti utilisée.",
      );
    }

    await stopLocationSharing(false);

    try {
      const connection = createRideHubConnection(token);
      await connection.start();
      locationConnectionRef.current = connection;
      setTrackingRideId(rideId);
      setMessage(
        "Partage de position chauffeur démarré. Position test Djibouti utilisée si le GPS est hors zone.",
      );

      await sendDriverLocation(connection, {
        rideId,
        latitude: defaultDjiboutiDriverLocation.latitude,
        longitude: defaultDjiboutiDriverLocation.longitude,
      }).catch(showError(setMessage));

      if (!navigator.geolocation) {
        return;
      }

      let lastSentAt = 0;
      locationWatchIdRef.current = navigator.geolocation.watchPosition(
        async (position) => {
          const now = Date.now();
          if (now - lastSentAt < 5000) {
            return;
          }
          lastSentAt = now;

          const point = normalizeDriverLocation({
            latitude: position.coords.latitude,
            longitude: position.coords.longitude,
          });

          await sendDriverLocation(connection, {
            rideId,
            latitude: point.latitude,
            longitude: point.longitude,
            heading: position.coords.heading,
            speed: position.coords.speed,
          }).catch(showError(setMessage));
        },
        (error) => setMessage(error.message),
        { enableHighAccuracy: true, maximumAge: 3000, timeout: 10000 },
      );

      locationStopTimerRef.current = window.setTimeout(
        () => {
          void stopLocationSharing();
        },
        30 * 60 * 1000,
      );
    } catch (error) {
      await stopLocationSharing(false);
      showError(setMessage)(error);
    }
  };

  return (
    <SpaceShell
      title="Espace Chauffeur"
      subtitle="Disponibilité, courses en attente et cycle de course."
      icon="🚖"
    >
      {message && <Notice>{message}</Notice>}
      <div className="grid gap-5 lg:grid-cols-[360px_1fr]">
        <section className="card p-6">
          <h2 className="section-title">Profil chauffeur</h2>
          <form
            className="grid gap-4"
            onSubmit={(event) => {
              event.preventDefault();
              action(
                () =>
                  api.createDriver(
                    {
                      licenseNumber,
                      vehiclePlate,
                      vehicleType,
                    },
                    token,
                  ),
                "Profil chauffeur créé ou mis à jour.",
              );
            }}
          >
            <Field
              label="Numéro de permis"
              value={licenseNumber}
              onChange={setLicenseNumber}
            />
            <Field
              label="Plaque"
              value={vehiclePlate}
              onChange={setVehiclePlate}
            />
            <Field
              label="Véhicule"
              value={vehicleType}
              onChange={setVehicleType}
            />
            <button className="btn-primary">Créer un profil</button>
          </form>
          <button
            className="btn-yellow mt-4 w-full"
            onClick={() =>
              action(
                () => api.setAvailability(true, token),
                "Vous êtes disponible.",
              )
            }
          >
            Me mettre disponible
          </button>
        </section>
        <RideList
          rides={pendingRides}
          title="Courses en attente"
          renderAction={(ride) => (
            <button
              className="btn-yellow"
              onClick={() =>
                action(() => api.acceptRide(ride.id, token), "Course acceptée.")
              }
            >
              Accepter
            </button>
          )}
        />
      </div>
      <RideList
        rides={myRides}
        title="Mes courses chauffeur"
        renderAction={(ride) => (
          <DriverRideAction
            ride={ride}
            action={action}
            token={token}
            trackingRideId={trackingRideId}
            startLocationSharing={startLocationSharing}
          />
        )}
      />
    </SpaceShell>
  );
}

function AdminSpace({ session }: { session: AuthResponse }) {
  const [stats, setStats] = useState<AdminStats | null>(null);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [drivers, setDrivers] = useState<DriverProfile[]>([]);
  const [rides, setRides] = useState<Ride[]>([]);
  const [message, setMessage] = useState("");
  const [driverUserId, setDriverUserId] = useState("");
  const [licenseNumber, setLicenseNumber] = useState("LIC-ADMIN");
  const [vehiclePlate, setVehiclePlate] = useState("DJ-0001");
  const [vehicleType, setVehicleType] = useState("Taxi");
  const [adminDriverLocations, setAdminDriverLocations] = useState<
    Record<number, DriverLocationPayload>
  >({});
  const token = session.accessToken;

  const refresh = async () => {
    const [nextStats, nextUsers, nextDrivers, nextRides] = await Promise.all([
      api.adminStats(token),
      api.adminUsers(token),
      api.adminDrivers(token),
      api.adminRides(token),
    ]);
    setStats(nextStats);
    setUsers(nextUsers);
    setDrivers(nextDrivers);
    setRides(nextRides);
  };

  useEffect(() => {
    refresh().catch(showError(setMessage));
  }, [token]);

  useEffect(() => {
    const connection = createRideHubConnection(token);

    connection.on("driverLocationUpdated", (payload: DriverLocationPayload) => {
      const key = payload.driverId ?? payload.rideId;
      setAdminDriverLocations((current) => ({ ...current, [key]: payload }));
    });

    connection
      .start()
      .then(() => joinAdminLocationGroup(connection))
      .catch(showError(setMessage));

    return () => {
      connection.stop().catch(() => undefined);
    };
  }, [token]);

  const createDriver = (event: React.FormEvent) => {
    event.preventDefault();
    run(
      setMessage,
      async () => {
        await api.createDriver(
          {
            userId: Number(driverUserId),
            licenseNumber,
            vehiclePlate,
            vehicleType,
          },
          token,
        );
        await refresh();
      },
      "Chauffeur créé ou mis à jour.",
    );
  };

  return (
    <SpaceShell
      title="Espace Admin"
      subtitle="Utilisateurs, chauffeurs, courses et statistiques du MVP."
      icon="🛡️"
    >
      {message && <Notice>{message}</Notice>}
      <section className="grid gap-4 md:grid-cols-4">
        <StatCard label="Utilisateurs" value={stats?.users ?? 0} />
        <StatCard label="Chauffeurs" value={stats?.drivers ?? 0} />
        <StatCard label="Courses" value={stats?.rides ?? 0} />
        <StatCard label="Signalements" value={stats?.reports ?? 0} />
      </section>
      <AdminLiveMap locations={Object.values(adminDriverLocations)} />
      <div className="grid gap-5 lg:grid-cols-[360px_1fr]">
        <section className="card p-6">
          <h2 className="section-title">Créer un chauffeur</h2>
          <form className="grid gap-4" onSubmit={createDriver}>
            <Field
              label="ID utilisateur Driver"
              value={driverUserId}
              onChange={setDriverUserId}
            />
            <Field
              label="Numéro de permis"
              value={licenseNumber}
              onChange={setLicenseNumber}
            />
            <Field
              label="Plaque"
              value={vehiclePlate}
              onChange={setVehiclePlate}
            />
            <Field
              label="Véhicule"
              value={vehicleType}
              onChange={setVehicleType}
            />
            <button className="btn-primary">Créer / mettre à jour</button>
          </form>
        </section>
        <SimpleTable
          title="Utilisateurs"
          rows={users.map((u) => [
            u.id,
            u.fullName,
            `${u.phoneNumber} · ${u.roles.join(", ")}`,
          ])}
        />
      </div>
      <SimpleTable
        title="Chauffeurs"
        rows={drivers.map((d) => [
          `#${d.userId}`,
          d.vehiclePlate,
          d.isAvailable ? "Disponible" : "Indisponible",
        ])}
      />
      <RideList rides={rides} title="Toutes les courses" />
    </SpaceShell>
  );
}

function DriverRideAction({
  ride,
  action,
  token,
  trackingRideId,
  startLocationSharing,
}: {
  ride: Ride;
  action: (callback: () => Promise<unknown>, success: string) => void;
  token: string;
  trackingRideId: number | null;
  startLocationSharing: (rideId: number) => Promise<void>;
}) {
  const canShareLocation = ["Accepted", "DriverArrived", "InProgress"].includes(
    ride.status,
  );

  return (
    <div className="flex flex-wrap gap-2">
      {canShareLocation && (
        <button
          className="btn-secondary"
          onClick={() => startLocationSharing(ride.id)}
        >
          {trackingRideId === ride.id ? "Position active" : "Partager position"}
        </button>
      )}
      {ride.status === "Accepted" && (
        <button
          className="btn-yellow"
          onClick={() =>
            action(
              () => api.updateRideStatus(ride.id, "arrived", token),
              "Arrivée confirmée.",
            )
          }
        >
          Arrivé
        </button>
      )}
      {ride.status === "DriverArrived" && (
        <button
          className="btn-yellow"
          onClick={() =>
            action(
              () => api.updateRideStatus(ride.id, "start", token),
              "Course commencée.",
            )
          }
        >
          Commencer
        </button>
      )}
      {ride.status === "InProgress" && (
        <button
          className="btn-yellow"
          onClick={() =>
            action(
              () => api.updateRideStatus(ride.id, "complete", token),
              "Course terminée.",
            )
          }
        >
          Terminer
        </button>
      )}
    </div>
  );
}

function normalizeDriverLocation(point: MapPoint): MapPoint {
  const roundedPoint = {
    latitude: Number(point.latitude.toFixed(6)),
    longitude: Number(point.longitude.toFixed(6)),
  };

  return distanceInKm(roundedPoint, defaultDjiboutiDriverLocation) >
    djiboutiDriverFallbackRadiusKm
    ? defaultDjiboutiDriverLocation
    : roundedPoint;
}

function distanceInKm(from: MapPoint, to: MapPoint) {
  const earthRadiusKm = 6371;
  const dLat = degreesToRadians(to.latitude - from.latitude);
  const dLon = degreesToRadians(to.longitude - from.longitude);
  const lat1 = degreesToRadians(from.latitude);
  const lat2 = degreesToRadians(to.latitude);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.sin(dLon / 2) ** 2 * Math.cos(lat1) * Math.cos(lat2);
  return 2 * earthRadiusKm * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

function degreesToRadians(value: number) {
  return (value * Math.PI) / 180;
}

function Protected({
  session,
  role,
  children,
}: {
  session: AuthResponse | null;
  role: UserRole;
  children: React.ReactNode;
}) {
  if (!session) return <Navigate to="/login" replace />;
  const userRole = primaryRole(session.user);
  if (userRole !== role)
    return (
      <Navigate
        to={
          userRole === "Admin"
            ? "/admin"
            : userRole === "Driver"
              ? "/chauffeur"
              : "/client"
        }
        replace
      />
    );
  return children;
}

function SpaceShell({
  title,
  subtitle,
  icon,
  children,
}: {
  title: string;
  subtitle: string;
  icon: string;
  children: React.ReactNode;
}) {
  return (
    <section className="grid gap-5">
      <div className="card flex items-center gap-4 p-6">
        <span className="text-5xl">{icon}</span>
        <div>
          <h1 className="text-3xl font-black tracking-tight md:text-4xl">
            {title}
          </h1>
          <p className="text-slate-600">{subtitle}</p>
        </div>
      </div>
      {children}
    </section>
  );
}

function RideList({
  rides,
  title,
  renderAction,
}: {
  rides: Ride[];
  title: string;
  renderAction?: (ride: Ride) => React.ReactNode;
}) {
  return (
    <section className="card p-6">
      <h2 className="section-title">{title}</h2>
      <div className="grid gap-3">
        {rides.length === 0 && (
          <p className="text-slate-500">Aucune course à afficher.</p>
        )}
        {rides.map((ride) => (
          <article
            key={ride.id}
            className="grid gap-3 rounded-3xl border border-slate-200 bg-white p-4 md:grid-cols-[1fr_auto_auto] md:items-center"
          >
            <div>
              <strong>
                #{ride.id} · {ride.pickupAddress} → {ride.destinationAddress}
              </strong>
              <p className="text-sm text-slate-600">
                {ride.pickupZone} vers {ride.destinationZone} ·{" "}
                {ride.estimatedPrice} FDJ
              </p>
              <p className="text-xs text-slate-500">
                Client : {ride.clientId} · Chauffeur :{" "}
                {ride.driverId ?? "non assigné"}
              </p>
              <RideTimeline ride={ride} />
            </div>
            <span className={`status status-${ride.status.toLowerCase()}`}>
              {ride.status}
            </span>
            {renderAction?.(ride)}
          </article>
        ))}
      </div>
    </section>
  );
}

function AdminLiveMap({ locations }: { locations: DriverLocationPayload[] }) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<Map<number, L.Marker>>(new Map());

  useEffect(() => {
    if (!containerRef.current || mapRef.current) {
      return;
    }

    const map = L.map(containerRef.current, {
      center: [
        defaultDjiboutiDriverLocation.latitude,
        defaultDjiboutiDriverLocation.longitude,
      ],
      zoom: 12,
      maxBounds: djiboutiMapBounds,
      maxBoundsViscosity: 1,
      minZoom: 11,
      scrollWheelZoom: true,
    });

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "&copy; OpenStreetMap contributors",
      maxZoom: 19,
    }).addTo(map);

    mapRef.current = map;

    return () => {
      markersRef.current.forEach((marker) => marker.remove());
      markersRef.current.clear();
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) {
      return;
    }

    const activeKeys = new Set<number>();

    locations.forEach((location) => {
      const key = location.driverId ?? location.rideId;
      activeKeys.add(key);
      const latLng = normalizeAdminLocation(location);
      const popup = `Chauffeur #${location.driverId ?? "inconnu"}<br>Course #${location.rideId}<br>${latLng.lat.toFixed(6)}, ${latLng.lng.toFixed(6)}`;
      const existingMarker = markersRef.current.get(key);

      if (existingMarker) {
        existingMarker.setLatLng(latLng).setPopupContent(popup);
      } else {
        markersRef.current.set(
          key,
          L.marker(latLng, { icon: adminTaxiIcon() })
            .addTo(map)
            .bindPopup(popup),
        );
      }
    });

    markersRef.current.forEach((marker, key) => {
      if (!activeKeys.has(key)) {
        marker.remove();
        markersRef.current.delete(key);
      }
    });
  }, [locations]);

  return (
    <section className="card p-6">
      <h2 className="section-title">Carte Admin live</h2>
      <div className="overflow-hidden rounded-[1.75rem] border border-slate-200">
        <div
          ref={containerRef}
          className="h-[320px] w-full"
          aria-label="Carte admin des chauffeurs actifs"
        />
      </div>
      <div className="mt-3 grid gap-2 text-sm text-slate-600 md:grid-cols-2">
        {locations.length === 0 && (
          <p className="rounded-2xl bg-slate-50 p-3 md:col-span-2">
            Aucune position chauffeur active reçue pour le moment.
          </p>
        )}
        {locations.map((location) => (
          <p
            key={`${location.driverId ?? "driver"}-${location.rideId}`}
            className="rounded-2xl bg-blue-50 p-3 text-blue-800"
          >
            Chauffeur #{location.driverId ?? "inconnu"} · course #
            {location.rideId}
            <br />
            {location.latitude.toFixed(6)}, {location.longitude.toFixed(6)}
          </p>
        ))}
      </div>
    </section>
  );
}

function normalizeAdminLocation(location: DriverLocationPayload) {
  const latLng = L.latLng(location.latitude, location.longitude);
  return djiboutiMapBounds.contains(latLng)
    ? latLng
    : L.latLng(
        defaultDjiboutiDriverLocation.latitude,
        defaultDjiboutiDriverLocation.longitude,
      );
}

function adminTaxiIcon() {
  return L.divIcon({
    className: "taxi-map-marker",
    html: '<span style="background:#2563eb">🚕</span>',
    iconSize: [38, 38],
    iconAnchor: [19, 38],
    popupAnchor: [0, -38],
  });
}

function RideTimeline({ ride }: { ride: Ride }) {
  const events = [
    { label: "Demandée", value: ride.createdAt },
    { label: "Acceptée", value: ride.acceptedAt },
    { label: "Terminée", value: ride.completedAt },
  ].filter((event) => Boolean(event.value));

  if (events.length === 0) {
    return null;
  }

  return (
    <ol className="mt-3 flex flex-wrap gap-2 text-[11px] font-bold text-slate-500">
      {events.map((event) => (
        <li key={event.label} className="rounded-full bg-slate-100 px-3 py-1">
          {event.label} · {formatDateTime(event.value)}
        </li>
      ))}
    </ol>
  );
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("fr-FR", {
    dateStyle: "short",
    timeStyle: "short",
  }).format(date);
}

function Field({
  label,
  value,
  onChange,
  type = "text",
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  type?: string;
}) {
  return (
    <label className="field-label">
      {label}
      <input
        className="field"
        type={type}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        required
      />
    </label>
  );
}

function CoordinatePreview({
  label,
  point,
}: {
  label: string;
  point: MapPoint | null;
}) {
  return (
    <div className="rounded-2xl bg-slate-50 p-3 text-sm text-slate-600">
      <strong>{label}</strong>
      <br />
      {point
        ? `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)}`
        : "Non sélectionnées"}
    </div>
  );
}

function ZoneSelect({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="field-label">
      {label}
      <select
        className="field"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {zones.map((zone) => (
          <option key={zone} value={zone}>
            {zone}
          </option>
        ))}
      </select>
    </label>
  );
}

function FeatureCard({ title, items }: { title: string; items: string[] }) {
  return (
    <section className="card p-6">
      <h2 className="section-title">{title}</h2>
      <ul className="grid gap-2 text-slate-600">
        {items.map((item) => (
          <li key={item}>✅ {item}</li>
        ))}
      </ul>
    </section>
  );
}

function FuturePanel({ title, items }: { title: string; items: string[] }) {
  return (
    <section className="card bg-taxi-navy p-6 text-white">
      <h2 className="section-title">{title}</h2>
      <ul className="grid gap-3 text-blue-100">
        {items.map((item) => (
          <li key={item}>• {item}</li>
        ))}
      </ul>
    </section>
  );
}

function StatCard({ label, value }: { label: string; value: number }) {
  return (
    <article className="card p-5">
      <span className="font-bold text-slate-500">{label}</span>
      <strong className="block text-4xl font-black">{value}</strong>
    </article>
  );
}

function SimpleTable({ title, rows }: { title: string; rows: string[][] }) {
  return (
    <section className="card overflow-hidden p-6">
      <h2 className="section-title">{title}</h2>
      <div className="grid gap-2">
        {rows.length === 0 && <p className="text-slate-500">Aucune donnée.</p>}
        {rows.map((row, index) => (
          <div
            className="grid gap-2 rounded-2xl bg-white p-3 text-sm md:grid-cols-3"
            key={`${title}-${index}`}
          >
            {row.map((cell) => (
              <span key={cell}>{cell}</span>
            ))}
          </div>
        ))}
      </div>
    </section>
  );
}

function NavLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link
      className="rounded-full px-4 py-2 font-bold text-slate-600 hover:bg-white"
      to={to}
    >
      {children}
    </Link>
  );
}

function Notice({ children }: { children: React.ReactNode }) {
  return (
    <p className="rounded-3xl bg-yellow-100 p-4 font-bold text-yellow-800">
      {children}
    </p>
  );
}

function estimatePrice(from: string, to: string) {
  if (
    (from === "Centre-ville" && to === "Balbala") ||
    (from === "Balbala" && to === "Centre-ville")
  )
    return 1500;
  if (from === "Aéroport" && to === "Centre-ville") return 2500;
  if (from === "Héron" && to === "Centre-ville") return 1200;
  return 1000;
}

async function run(
  setMessage: (message: string) => void,
  callback: () => Promise<unknown>,
  success: string,
) {
  setMessage("");
  try {
    await callback();
    setMessage(success);
  } catch (err) {
    showError(setMessage)(err);
  }
}

function showError(setMessage: (message: string) => void) {
  return (err: unknown) =>
    setMessage(err instanceof Error ? err.message : "Action impossible");
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
