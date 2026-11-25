import { useEffect, useState } from 'react';
import EmptyState from '../components/EmptyState';
import RouteMap from '../components/RouteMap';
import { PageHeader } from './SummaryPage';

function MapsPage() {
    const [turnos, setTurnos] = useState([]);
    const [message, setMessage] = useState(null);

    useEffect(() => {
        let isMounted = true;
        const loadMonitoring = async () => {
            try {
                const response = await fetch('/api/routes/monitoring');
                if (!response.ok) throw new Error('No fue posible cargar las rutas en el mapa.');
                const data = await response.json();
                if (isMounted) {
                    setTurnos(data.turnos);
                    setMessage(null);
                }
            } catch (error) {
                if (isMounted) setMessage(error.message);
            }
        };

        loadMonitoring();
        const intervalId = window.setInterval(loadMonitoring, 10000);
        return () => {
            isMounted = false;
            window.clearInterval(intervalId);
        };
    }, []);

    function formatLocationTime(value) {
        const timestamp = value.endsWith('Z') ? value : `${value}Z`;
        return new Date(timestamp).toLocaleString('es-MX', { timeZone: 'America/Hermosillo' });
    }

    function isLocationStale(value) {
        const timestamp = value.endsWith('Z') ? value : `${value}Z`;
        return Date.now() - new Date(timestamp).getTime() > 120000;
    }

    return <>
        <PageHeader title="Mapa de rutas" subtitle="Ubicación GPS del repartidor actualizada cada 10 segundos." status />
        <section className="content-section map-page">
            {message && <div className="operation-message error">{message}</div>}
            {turnos.length === 0 ? <section className="panel"><EmptyState title="No hay rutas para mostrar" description="Las rutas guardadas aparecerán aquí con su recorrido y paradas." /></section> : <div className="map-route-grid">{turnos.map((turn) => {
                const location = turn.ultimaUbicacion;
                const stale = location ? isLocationStale(location.fechaHoraUtc) : false;
                const points = location ? [{ latitude: location.latitud, longitude: location.longitud }] : [];
                return <article className="panel map-route-card" key={turn.idTurno}>
                    <div className="section-heading"><h2>Turno #{turn.idTurno} · {turn.repartidor}</h2><p>{turn.unidad} · {turn.estadoTurno} · {turn.totalPaquetes} paquete(s)</p></div>
                    <div className={`driver-location ${location ? (stale ? 'stale' : 'live') : 'waiting'}`}>
                        {location
                            ? <><strong>{stale ? 'Ubicación desactualizada' : 'Ubicación en vivo'}</strong><span>{location.latitud.toFixed(5)}, {location.longitud.toFixed(5)} · Actualizada: {formatLocationTime(location.fechaHoraUtc)}</span></>
                            : <><strong>Esperando ubicación GPS</strong><span>El repartidor debe iniciar la ruta y permitir el acceso a su ubicación.</span></>}
                    </div>
                    {turn.geometria || location
                        ? <RouteMap routeGeometry={turn.geometria} points={points} />
                        : <EmptyState title="Ruta sin geometría" description="No se guardó una geometría para este turno." />}
                    <div className="map-route-metrics"><span>{turn.distanciaMetros ? `${(turn.distanciaMetros / 1000).toFixed(1)} km` : 'Sin distancia'}</span><span>{turn.duracionSegundos ? `${Math.round(turn.duracionSegundos / 60)} min` : 'Sin duración'}</span></div>
                </article>;
            })}</div>}
        </section>
    </>;
}

export default MapsPage;
