import { useEffect, useState } from 'react';
import EmptyState from '../components/EmptyState';
import RouteMap from '../components/RouteMap';
import { PageHeader } from './SummaryPage';

function MapsPage() {
    const [turnos, setTurnos] = useState([]);
    const [message, setMessage] = useState(null);

    useEffect(() => {
        fetch('/api/routes/monitoring')
            .then(async (response) => {
                if (!response.ok) throw new Error('No fue posible cargar las rutas en el mapa.');
                setTurnos((await response.json()).turnos);
            })
            .catch((error) => setMessage(error.message));
    }, []);

    return <>
        <PageHeader title="Mapa de rutas" subtitle="Visualiza las rutas calculadas con OSRM sobre OpenStreetMap." status />
        <section className="content-section map-page">
            {message && <div className="operation-message error">{message}</div>}
            {turnos.length === 0 ? <section className="panel"><EmptyState title="No hay rutas para mostrar" description="Las rutas guardadas aparecerán aquí con su recorrido y paradas." /></section> : <div className="map-route-grid">{turnos.map((turn) => <article className="panel map-route-card" key={turn.idTurno}><div className="section-heading"><h2>Turno #{turn.idTurno} · {turn.repartidor}</h2><p>{turn.unidad} · {turn.estadoTurno} · {turn.totalPaquetes} paquete(s)</p></div>{turn.geometria ? <RouteMap routeGeometry={turn.geometria} /> : <EmptyState title="Ruta sin geometría" description="No se guardó una geometría para este turno." />}<div className="map-route-metrics"><span>{turn.distanciaMetros ? `${(turn.distanciaMetros / 1000).toFixed(1)} km` : 'Sin distancia'}</span><span>{turn.duracionSegundos ? `${Math.round(turn.duracionSegundos / 60)} min` : 'Sin duración'}</span></div></article>)}</div>}
        </section>
    </>;
}

export default MapsPage;
