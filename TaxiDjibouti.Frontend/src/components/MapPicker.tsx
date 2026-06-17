import { useEffect, useMemo, useRef, useState, type MutableRefObject } from 'react';
import L from 'leaflet';
export interface MapPoint {
  latitude: number;
  longitude: number;
}

interface MapPickerProps {
  pickup?: MapPoint | null;
  destination?: MapPoint | null;
  driverLocation?: MapPoint | null;
  onPickupChange: (point: MapPoint) => void;
  onDestinationChange: (point: MapPoint) => void;
}

interface AddressResult {
  displayName: string;
  point: MapPoint;
}

const djiboutiCenter: L.LatLngExpression = [11.5721, 43.1456];
const djiboutiBounds = L.latLngBounds([11.35, 42.85], [11.85, 43.45]);
const pickupIcon = createMarkerIcon("#16a34a", "D");
const destinationIcon = createMarkerIcon("#dc2626", "A");
const driverIcon = createTaxiIcon();
const djiboutiRadiusKm = 70;
const averageTaxiSpeedKmH = 35;

export function MapPicker({
  pickup,
  destination,
  driverLocation,
  onPickupChange,
  onDestinationChange,
}: MapPickerProps) {
  const [pickupSearch, setPickupSearch] = useState("");
  const [destinationSearch, setDestinationSearch] = useState("");
  const [pickupResults, setPickupResults] = useState<AddressResult[]>([]);
  const [destinationResults, setDestinationResults] = useState<AddressResult[]>(
    [],
  );
  const [searchMessage, setSearchMessage] = useState("");
  const [isSearchingPickup, setIsSearchingPickup] = useState(false);
  const [isSearchingDestination, setIsSearchingDestination] = useState(false);
  const [isLocatingClient, setIsLocatingClient] = useState(false);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const pickupMarkerRef = useRef<L.Marker | null>(null);
  const destinationMarkerRef = useRef<L.Marker | null>(null);
  const driverMarkerRef = useRef<L.Marker | null>(null);
  const routeLineRef = useRef<L.Polyline | null>(null);
  const driverLineRef = useRef<L.Polyline | null>(null);
  const driverAnimationRef = useRef<number | null>(null);
  const nextPointRef = useRef<"pickup" | "destination">("pickup");

  const pickupToDestinationDistance = useMemo(
    () => (pickup && destination ? distanceInKm(pickup, destination) : null),
    [destination, pickup],
  );
  const driverToPickupDistance = useMemo(
    () =>
      driverLocation && pickup ? distanceInKm(driverLocation, pickup) : null,
    [driverLocation, pickup],
  );
  const driverOutsideDjibouti = Boolean(
    driverLocation &&
    distanceInKm(driverLocation, toMapPoint(djiboutiCenter)) > djiboutiRadiusKm,
  );

  useEffect(() => {
    if (!containerRef.current || mapRef.current) {
      return;
    }

    const map = L.map(containerRef.current, {
      center: djiboutiCenter,
      zoom: 13,
      scrollWheelZoom: true,
      maxBounds: djiboutiBounds,
      maxBoundsViscosity: 1,
      minZoom: 11,
    });

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "&copy; OpenStreetMap contributors",
      maxZoom: 19,
    }).addTo(map);

    map.on("click", (event: L.LeafletMouseEvent) => {
      if (!djiboutiBounds.contains(event.latlng)) {
        return;
      }

      const point = {
        latitude: Number(event.latlng.lat.toFixed(6)),
        longitude: Number(event.latlng.lng.toFixed(6)),
      };

      if (nextPointRef.current === "pickup") {
        onPickupChange(point);
        nextPointRef.current = "destination";
      } else {
        onDestinationChange(point);
        nextPointRef.current = "pickup";
      }
    });

    mapRef.current = map;

    return () => {
      if (driverAnimationRef.current !== null) {
        window.cancelAnimationFrame(driverAnimationRef.current);
      }
      map.remove();
      mapRef.current = null;
      pickupMarkerRef.current = null;
      destinationMarkerRef.current = null;
      driverMarkerRef.current = null;
      routeLineRef.current = null;
      driverLineRef.current = null;
    };
  }, [onDestinationChange, onPickupChange]);

  useEffect(() => {
    syncMarker({
      map: mapRef.current,
      markerRef: pickupMarkerRef,
      point: pickup,
      icon: pickupIcon,
      label: "Départ",
    });
  }, [pickup]);

  useEffect(() => {
    syncMarker({
      map: mapRef.current,
      markerRef: destinationMarkerRef,
      point: destination,
      icon: destinationIcon,
      label: "Arrivée",
    });
  }, [destination]);

  useEffect(() => {
    syncMarker({
      map: mapRef.current,
      markerRef: driverMarkerRef,
      point: driverLocation,
      icon: driverIcon,
      label: "Chauffeur",
      animate: true,
      animationRef: driverAnimationRef,
    });
  }, [driverLocation]);

  useEffect(() => {
    syncLine({
      map: mapRef.current,
      lineRef: routeLineRef,
      points: pickup && destination ? [pickup, destination] : [],
      color: "#f97316",
      dashArray: undefined,
    });
  }, [destination, pickup]);

  useEffect(() => {
    syncLine({
      map: mapRef.current,
      lineRef: driverLineRef,
      points: driverLocation && pickup ? [driverLocation, pickup] : [],
      color: "#2563eb",
      dashArray: "6 8",
    });
  }, [driverLocation, pickup]);

  useEffect(() => {
    fitVisiblePoints(mapRef.current, [pickup, destination, driverLocation]);
  }, [destination, driverLocation, pickup]);

  async function searchAddress(kind: "pickup" | "destination") {
    const query = kind === "pickup" ? pickupSearch : destinationSearch;
    const setResults =
      kind === "pickup" ? setPickupResults : setDestinationResults;
    const setIsSearching =
      kind === "pickup" ? setIsSearchingPickup : setIsSearchingDestination;

    if (!query.trim()) {
      setSearchMessage("Saisis une adresse à chercher dans Djibouti.");
      return;
    }

    setIsSearching(true);
    setSearchMessage("");

    try {
      const results = await searchDjiboutiAddress(query);
      setResults(results);
      setSearchMessage(
        results.length === 0
          ? "Aucun résultat trouvé dans la zone Djibouti."
          : "",
      );
    } catch {
      setSearchMessage(
        "Recherche adresse impossible pour le moment. Réessaie ou clique sur la carte.",
      );
    } finally {
      setIsSearching(false);
    }
  }

  function useClientLocationAsPickup() {
    setSearchMessage("");
    setIsLocatingClient(true);

    const applyPoint = (point: MapPoint, message: string) => {
      onPickupChange(point);
      setPickupSearch("Ma position actuelle");
      setPickupResults([]);
      nextPointRef.current = "destination";
      setSearchMessage(message);
      fitVisiblePoints(mapRef.current, [point, destination, driverLocation]);
      setIsLocatingClient(false);
    };

    if (!navigator.geolocation) {
      applyPoint(
        toMapPoint(djiboutiCenter),
        "Géolocalisation indisponible : départ placé au centre de Djibouti.",
      );
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (position) => {
        const browserPoint = {
          latitude: Number(position.coords.latitude.toFixed(6)),
          longitude: Number(position.coords.longitude.toFixed(6)),
        };
        const point = normalizePointToDjibouti(browserPoint);
        const message =
          point === browserPoint
            ? "Position client utilisée comme départ."
            : "Position GPS hors Djibouti : départ placé au centre de Djibouti pour le test.";
        applyPoint(point, message);
      },
      (error) => {
        applyPoint(
          toMapPoint(djiboutiCenter),
          `${error.message} Départ placé au centre de Djibouti pour le test.`,
        );
      },
      { enableHighAccuracy: true, maximumAge: 5000, timeout: 10000 },
    );
  }

  function selectSearchResult(
    kind: "pickup" | "destination",
    result: AddressResult,
  ) {
    if (kind === "pickup") {
      onPickupChange(result.point);
      setPickupSearch(result.displayName);
      setPickupResults([]);
      nextPointRef.current = "destination";
    } else {
      onDestinationChange(result.point);
      setDestinationSearch(result.displayName);
      setDestinationResults([]);
      nextPointRef.current = "pickup";
    }
  }

  return (
    <div className="grid gap-3">
      <div className="grid gap-3 rounded-[1.75rem] border border-slate-200 bg-slate-50 p-3 lg:grid-cols-2">
        <AddressSearchBox
          label="Recherche départ"
          placeholder="Ex : Place Menelik"
          value={pickupSearch}
          onChange={setPickupSearch}
          onSearch={() => searchAddress("pickup")}
          loading={isSearchingPickup}
          results={pickupResults}
          onSelect={(result) => selectSearchResult("pickup", result)}
        />
        <AddressSearchBox
          label="Recherche destination"
          placeholder="Ex : Aéroport"
          value={destinationSearch}
          onChange={setDestinationSearch}
          onSearch={() => searchAddress("destination")}
          loading={isSearchingDestination}
          results={destinationResults}
          onSelect={(result) => selectSearchResult("destination", result)}
        />
        <button
          type="button"
          className="btn-yellow lg:col-span-2"
          onClick={useClientLocationAsPickup}
          disabled={isLocatingClient}
        >
          {isLocatingClient
            ? "Recherche GPS..."
            : "📍 Utiliser ma position comme départ"}
        </button>
        {searchMessage && (
          <p className="text-sm font-bold text-amber-700 lg:col-span-2">
            {searchMessage}
          </p>
        )}
      </div>

      <div className="overflow-hidden rounded-[1.75rem] border border-slate-200">
        <div
          ref={containerRef}
          className="h-[360px] w-full"
          aria-label="Carte de sélection départ et destination"
        />
      </div>

      <div className="grid gap-2 text-sm text-slate-600 md:grid-cols-2">
        <p className="rounded-2xl bg-green-50 p-3 text-green-800">
          <strong>Départ :</strong>{" "}
          {formatPoint(pickup) ?? "Clique sur la carte ou cherche une adresse."}
        </p>
        <p className="rounded-2xl bg-red-50 p-3 text-red-800">
          <strong>Arrivée :</strong>{" "}
          {formatPoint(destination) ??
            "Le clic suivant choisit la destination."}
        </p>
        {pickupToDestinationDistance !== null && (
          <p className="rounded-2xl bg-orange-50 p-3 text-orange-800">
            <strong>Trajet :</strong>{" "}
            {formatDistance(pickupToDestinationDistance)} · environ{" "}
            {estimateMinutes(pickupToDestinationDistance)} min
          </p>
        )}
        {driverLocation && (
          <p className="rounded-2xl bg-blue-50 p-3 text-blue-800">
            <strong>Chauffeur :</strong> {formatPoint(driverLocation)}
            {driverToPickupDistance !== null && (
              <span className="mt-1 block">
                Distance vers départ : {formatDistance(driverToPickupDistance)}{" "}
                · environ {estimateMinutes(driverToPickupDistance)} min
              </span>
            )}
            {driverOutsideDjibouti && (
              <span className="mt-1 block text-xs font-bold">
                Position hors zone Djibouti : utilise une position GPS de test à
                Djibouti si tu testes depuis un autre pays.
              </span>
            )}
          </p>
        )}
      </div>
    </div>
  );
}

function AddressSearchBox({
  label,
  placeholder,
  value,
  onChange,
  onSearch,
  loading,
  results,
  onSelect,
}: {
  label: string;
  placeholder: string;
  value: string;
  onChange: (value: string) => void;
  onSearch: () => void;
  loading: boolean;
  results: AddressResult[];
  onSelect: (result: AddressResult) => void;
}) {
  return (
    <label className="grid gap-2 text-sm font-black text-slate-700">
      {label}
      <div className="flex gap-2">
        <input
          className="field min-w-0 flex-1"
          placeholder={placeholder}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              onSearch();
            }
          }}
        />
        <button
          className="btn-secondary shrink-0"
          type="button"
          onClick={onSearch}
          disabled={loading}
        >
          {loading ? "..." : "Chercher"}
        </button>
      </div>
      {results.length > 0 && (
        <div className="grid gap-1 rounded-2xl border border-slate-200 bg-white p-2 shadow-sm">
          {results.map((result) => (
            <button
              key={`${result.point.latitude}-${result.point.longitude}-${result.displayName}`}
              type="button"
              className="rounded-xl p-2 text-left text-xs font-bold text-slate-600 hover:bg-slate-100"
              onClick={() => onSelect(result)}
            >
              {result.displayName}
            </button>
          ))}
        </div>
      )}
    </label>
  );
}

function syncMarker({
  map,
  markerRef,
  point,
  icon,
  label,
  animate = false,
  animationRef,
}: {
  map: L.Map | null;
  markerRef: MutableRefObject<L.Marker | null>;
  point?: MapPoint | null;
  icon: L.DivIcon;
  label: string;
  animate?: boolean;
  animationRef?: MutableRefObject<number | null>;
}) {
  if (!map) {
    return;
  }

  if (!point) {
    if (markerRef.current) {
      markerRef.current.remove();
      markerRef.current = null;
    }
    return;
  }

  const latLng = L.latLng(point.latitude, point.longitude);

  if (!markerRef.current) {
    markerRef.current = L.marker(latLng, { icon }).addTo(map).bindPopup(label);
  } else if (animate && animationRef) {
    animateMarker(markerRef.current, latLng, animationRef);
  } else {
    markerRef.current.setLatLng(latLng);
  }

  markerRef.current.setPopupContent(`${label}<br>${formatPoint(point)}`);
}

function syncLine({
  map,
  lineRef,
  points,
  color,
  dashArray,
}: {
  map: L.Map | null;
  lineRef: MutableRefObject<L.Polyline | null>;
  points: MapPoint[];
  color: string;
  dashArray?: string;
}) {
  if (!map || points.length < 2) {
    if (lineRef.current) {
      lineRef.current.remove();
      lineRef.current = null;
    }
    return;
  }

  const latLngs = points.map((point) =>
    L.latLng(point.latitude, point.longitude),
  );

  if (!lineRef.current) {
    lineRef.current = L.polyline(latLngs, {
      color,
      dashArray,
      opacity: 0.85,
      weight: 4,
    }).addTo(map);
  } else {
    lineRef.current.setLatLngs(latLngs);
  }
}

function fitVisiblePoints(
  map: L.Map | null,
  points: Array<MapPoint | null | undefined>,
) {
  if (!map) {
    return;
  }

  const latLngs = points
    .filter((point): point is MapPoint => Boolean(point))
    .map((point) => L.latLng(point.latitude, point.longitude))
    .filter((point) => djiboutiBounds.contains(point));

  window.setTimeout(() => map.invalidateSize(), 0);

  if (latLngs.length === 0) {
    map.fitBounds(djiboutiBounds, { padding: [20, 20] });
    return;
  }

  if (latLngs.length === 1) {
    map.flyTo(latLngs[0], Math.max(map.getZoom(), 13), { duration: 0.6 });
    return;
  }

  map.fitBounds(
    L.latLngBounds(latLngs).pad(0.2).extend(djiboutiBounds.getCenter()),
    {
      maxZoom: 13,
      padding: [32, 32],
    },
  );
}

function animateMarker(
  marker: L.Marker,
  nextLatLng: L.LatLng,
  animationRef: MutableRefObject<number | null>,
) {
  if (animationRef.current !== null) {
    window.cancelAnimationFrame(animationRef.current);
  }

  const startLatLng = marker.getLatLng();
  const startedAt = performance.now();
  const durationMs = 650;

  const step = (timestamp: number) => {
    const progress = Math.min((timestamp - startedAt) / durationMs, 1);
    const eased = 1 - (1 - progress) ** 3;
    const lat = startLatLng.lat + (nextLatLng.lat - startLatLng.lat) * eased;
    const lng = startLatLng.lng + (nextLatLng.lng - startLatLng.lng) * eased;
    marker.setLatLng([lat, lng]);

    if (progress < 1) {
      animationRef.current = window.requestAnimationFrame(step);
    } else {
      animationRef.current = null;
    }
  };

  animationRef.current = window.requestAnimationFrame(step);
}

function normalizePointToDjibouti(point: MapPoint) {
  return djiboutiBounds.contains(L.latLng(point.latitude, point.longitude))
    ? point
    : toMapPoint(djiboutiCenter);
}

async function searchDjiboutiAddress(query: string): Promise<AddressResult[]> {
  const params = new URLSearchParams({
    q: `${query}, Djibouti`,
    format: "jsonv2",
    addressdetails: "1",
    limit: "5",
    countrycodes: "dj",
    bounded: "1",
    viewbox: `${djiboutiBounds.getWest()},${djiboutiBounds.getNorth()},${djiboutiBounds.getEast()},${djiboutiBounds.getSouth()}`,
  });
  const response = await fetch(
    `https://nominatim.openstreetmap.org/search?${params.toString()}`,
    {
      headers: { Accept: "application/json" },
    },
  );

  if (!response.ok) {
    throw new Error("Address search failed");
  }

  const data = (await response.json()) as Array<{
    display_name: string;
    lat: string;
    lon: string;
  }>;
  return data
    .map((item) => ({
      displayName: item.display_name,
      point: {
        latitude: Number(Number(item.lat).toFixed(6)),
        longitude: Number(Number(item.lon).toFixed(6)),
      },
    }))
    .filter((item) =>
      djiboutiBounds.contains(
        L.latLng(item.point.latitude, item.point.longitude),
      ),
    );
}

function toMapPoint(point: L.LatLngExpression): MapPoint {
  const [latitude, longitude] = point as [number, number];
  return { latitude, longitude };
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

function estimateMinutes(distanceKm: number) {
  return Math.max(1, Math.round((distanceKm / averageTaxiSpeedKmH) * 60));
}

function degreesToRadians(value: number) {
  return (value * Math.PI) / 180;
}

function createMarkerIcon(color: string, label: string) {
  return L.divIcon({
    className: "taxi-map-marker",
    html: `<span style="background:${color}">${label}</span>`,
    iconSize: [34, 34],
    iconAnchor: [17, 34],
    popupAnchor: [0, -34],
  });
}

function createTaxiIcon() {
  return L.divIcon({
    className: "taxi-map-marker taxi-map-marker-taxi",
    html: '<span style="background:#2563eb">🚕</span>',
    iconSize: [38, 38],
    iconAnchor: [19, 38],
    popupAnchor: [0, -38],
  });
}

function formatPoint(point?: MapPoint | null) {
  if (!point) {
    return null;
  }

  return `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)}`;
}

function formatDistance(distanceKm: number) {
  return distanceKm < 1
    ? `${Math.round(distanceKm * 1000)} m`
    : `${distanceKm.toFixed(1)} km`;
}
