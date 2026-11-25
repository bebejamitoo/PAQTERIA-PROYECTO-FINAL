import 'leaflet/dist/leaflet.css';
import { CircleMarker, MapContainer, Polyline, Popup, TileLayer, useMap } from 'react-leaflet';
import { useEffect, useMemo, useState } from 'react';

const HERMOSILLO = [29.0729, -110.9559];

function FitMap({ points }) {
    const map = useMap();
    useEffect(() => {
        if (points.length > 1) map.fitBounds(points, { padding: [24, 24] });
    }, [map, points]);
    return null;
}

function getGeometryPoints(routeGeometry) {
    if (!routeGeometry) return [];
    try {
        const geometry = typeof routeGeometry === 'string' ? JSON.parse(routeGeometry) : routeGeometry;
        return geometry.coordinates.map(([longitude, latitude]) => [latitude, longitude]);
    } catch { return []; }
}

function RouteMap({ routeGeometry, points = [], height = 360 }) {
    const [previewGeometry, setPreviewGeometry] = useState(null);
    const pointCoordinates = useMemo(() => points
        .filter((point) => Number.isFinite(point.latitude) && Number.isFinite(point.longitude))
        .map(({ latitude, longitude }) => `${longitude},${latitude}`), [points]);
    const previewKey = pointCoordinates.join(';');
    const geometryPoints = useMemo(() => getGeometryPoints(routeGeometry ?? (previewGeometry?.key === previewKey ? previewGeometry.geometry : null)), [previewGeometry, previewKey, routeGeometry]);
    const markerPoints = useMemo(() => points
        .filter((point) => Number.isFinite(point.latitude) && Number.isFinite(point.longitude))
        .map(({ latitude, longitude }) => [latitude, longitude]), [points]);
    const linePoints = geometryPoints.length > 0 ? geometryPoints : markerPoints;
    const boundsPoints = geometryPoints.length > 0
        ? [...geometryPoints, ...markerPoints]
        : markerPoints;

    useEffect(() => {
        if (routeGeometry || pointCoordinates.length < 2) {
            return undefined;
        }

        const controller = new AbortController();
        fetch(`https://router.project-osrm.org/route/v1/driving/${pointCoordinates.join(';')}?overview=full&geometries=geojson&steps=false`, { signal: controller.signal })
            .then(async (response) => {
                if (!response.ok) throw new Error('No fue posible calcular la ruta vial.');
                const body = await response.json();
                if (body.code !== 'Ok' || !body.routes?.[0]?.geometry) throw new Error('OSRM no encontró una ruta vial.');
                setPreviewGeometry({ key: pointCoordinates.join(';'), geometry: body.routes[0].geometry });
            })
            .catch((error) => {
                if (error.name !== 'AbortError') setPreviewGeometry(null);
            });
        return () => controller.abort();
    }, [pointCoordinates, routeGeometry]);
    const center = boundsPoints[0] ?? HERMOSILLO;

    return <div className="route-map" style={{ height }}>
        <MapContainer center={center} zoom={12} scrollWheelZoom className="route-map-canvas">
            <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
            {linePoints.length > 1 && <Polyline positions={linePoints} pathOptions={{ color: '#1760d6', weight: 5 }} />}
            {points.filter((point) => Number.isFinite(point.latitude) && Number.isFinite(point.longitude)).map((point, index) => <CircleMarker key={`${point.latitude}-${point.longitude}-${index}`} center={[point.latitude, point.longitude]} radius={8} pathOptions={{ color: index === 0 ? '#0e9e64' : '#df3444', fillOpacity: 1 }}><Popup>{index === 0 ? 'Ubicación del repartidor' : `Parada ${index}`}</Popup></CircleMarker>)}
            <FitMap points={boundsPoints} />
        </MapContainer>
    </div>;
}

export default RouteMap;
