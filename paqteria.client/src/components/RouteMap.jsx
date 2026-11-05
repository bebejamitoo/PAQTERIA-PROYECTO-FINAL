import 'leaflet/dist/leaflet.css';
import { CircleMarker, MapContainer, Polyline, Popup, TileLayer, useMap } from 'react-leaflet';
import { useEffect, useMemo } from 'react';

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
    const geometryPoints = useMemo(() => getGeometryPoints(routeGeometry), [routeGeometry]);
    const mapPoints = geometryPoints.length > 0 ? geometryPoints : points.map(({ latitude, longitude }) => [latitude, longitude]);
    const center = mapPoints[0] ?? HERMOSILLO;

    return <div className="route-map" style={{ height }}>
        <MapContainer center={center} zoom={12} scrollWheelZoom className="route-map-canvas">
            <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
            {mapPoints.length > 1 && <Polyline positions={mapPoints} pathOptions={{ color: '#1760d6', weight: 5 }} />}
            {points.map((point, index) => <CircleMarker key={`${point.latitude}-${point.longitude}-${index}`} center={[point.latitude, point.longitude]} radius={8} pathOptions={{ color: index === 0 ? '#0e9e64' : '#df3444', fillOpacity: 1 }}><Popup>{index === 0 ? 'Ubicación del repartidor' : `Parada ${index}`}</Popup></CircleMarker>)}
            <FitMap points={mapPoints} />
        </MapContainer>
    </div>;
}

export default RouteMap;
