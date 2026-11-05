import { useCallback, useEffect, useMemo, useState } from 'react';
import EmptyState from '../components/EmptyState';
import RouteMap from '../components/RouteMap';
import { PageHeader } from './SummaryPage';

function RoutesPage({ onMonitoringChange }) {
    const today = new Date().toISOString().slice(0, 10);
    const [options, setOptions] = useState({ repartidores: [], unidades: [], paquetesDisponibles: [] });
    const [monitoring, setMonitoring] = useState({ turnos: [], paquetesEnRuta: 0, paradasPendientes: 0, incidenciasAbiertas: 0 });
    const [form, setForm] = useState({ idRepartidor: '', idUnidad: '', fechaTurno: today, tiempoEstimadoHoras: '8' });
    const [selectedPackages, setSelectedPackages] = useState([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [message, setMessage] = useState(null);
    const [origin, setOrigin] = useState(null);

    const loadRouteData = useCallback(async () => {
        setIsLoading(true);
        try {
            const [optionsResponse, monitoringResponse] = await Promise.all([
                fetch('/api/routes/options'), fetch('/api/routes/monitoring'),
            ]);
            if (!optionsResponse.ok || !monitoringResponse.ok) throw new Error('No fue posible cargar la operación de rutas.');
            setOptions(await optionsResponse.json());
            const monitoringData = await monitoringResponse.json();
            setMonitoring(monitoringData);
            onMonitoringChange?.(monitoringData);
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally { setIsLoading(false); }
    }, [onMonitoringChange]);

    useEffect(() => {
        // La carga inicial sincroniza la vista con la API protegida.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        loadRouteData();
        if (!navigator.geolocation) {
            return undefined;
        }
        const watchId = navigator.geolocation.watchPosition(
            ({ coords }) => setOrigin({ latitude: coords.latitude, longitude: coords.longitude }),
            () => setMessage({ type: 'error', text: 'Activa la ubicación del celular para calcular el origen de la ruta.' }),
            { enableHighAccuracy: true, maximumAge: 10000, timeout: 15000 },
        );
        return () => navigator.geolocation.clearWatch(watchId);
    }, [loadRouteData]);

    function togglePackage(idPaquete) {
        setSelectedPackages((current) => current.includes(idPaquete)
            ? current.filter((id) => id !== idPaquete) : [...current, idPaquete]);
    }

    function handleChange(event) {
        setForm((current) => ({ ...current, [event.target.name]: event.target.value }));
    }

    async function handleSubmit(event) {
        event.preventDefault();
        if (isSaving) return;
        setIsSaving(true);
        setMessage(null);
        try {
            if (!origin) throw new Error('Obtén la ubicación del repartidor antes de asignar la ruta.');
            const response = await fetch('/api/routes', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ...form, idRepartidor: Number(form.idRepartidor), idUnidad: Number(form.idUnidad), tiempoEstimadoHoras: Number(form.tiempoEstimadoHoras), idPaquetes: selectedPackages, origenLatitud: origin.latitude, origenLongitud: origin.longitude }),
            });
            const body = await response.json();
            if (!response.ok) throw new Error(body.message ?? 'No fue posible asignar la ruta.');
            setMessage({ type: 'success', text: `Turno #${body.idTurno} guardado con ${body.totalPaquetes} paquete(s).` });
            setSelectedPackages([]);
            await loadRouteData();
        } catch (error) { setMessage({ type: 'error', text: error.message }); }
        finally { setIsSaving(false); }
    }

    const selectedWeight = useMemo(() => options.paquetesDisponibles
        .filter((item) => selectedPackages.includes(item.idPaquete))
        .reduce((total, item) => total + item.pesoKg, 0), [options.paquetesDisponibles, selectedPackages]);
    const selectedPoints = useMemo(() => origin ? [origin, ...options.paquetesDisponibles
        .filter((item) => selectedPackages.includes(item.idPaquete))
        .map((item) => ({ latitude: item.latitud, longitude: item.longitud }))] : [], [options.paquetesDisponibles, origin, selectedPackages]);
    const displayedTurn = monitoring.turnos[0];

    return <>
        <PageHeader title="Asignación de rutas" subtitle="Asigna paquetes a un repartidor y monitorea sus paradas." status />
        <section className="content-section">
            {message && <div className={`operation-message ${message.type}`}>{message.text}</div>}
            <form className="package-form route-assignment-form" onSubmit={handleSubmit}>
                <div className="form-heading"><div><h2>Nueva asignación</h2><p>El turno, las paradas y el historial se guardan en una sola operación.</p></div></div>
                <RouteMap points={selectedPoints} height={280} />
                <p className="form-hint">{origin ? `Origen GPS: ${origin.latitude.toFixed(5)}, ${origin.longitude.toFixed(5)}` : 'Esperando ubicación GPS del repartidor...'}</p>
                <div className="form-grid">
                    <label>Repartidor<select name="idRepartidor" value={form.idRepartidor} onChange={handleChange} required><option value="">Selecciona un repartidor</option>{options.repartidores.map((item) => <option key={item.idRepartidor} value={item.idRepartidor}>{item.nombre}</option>)}</select></label>
                    <label>Unidad<select name="idUnidad" value={form.idUnidad} onChange={handleChange} required><option value="">Selecciona una unidad</option>{options.unidades.map((item) => <option key={item.idUnidad} value={item.idUnidad}>{item.codigoUnidad} · {item.placas}</option>)}</select></label>
                    <label>Fecha del turno<input name="fechaTurno" type="date" min={today} value={form.fechaTurno} onChange={handleChange} required /></label>
                    <label>Tiempo estimado (horas)<input name="tiempoEstimadoHoras" type="number" min="0.5" step="0.5" value={form.tiempoEstimadoHoras} onChange={handleChange} required /></label>
                </div>
                {options.unidades.length === 0 && <p className="form-hint">No hay unidades registradas en la tabla UNIDADES. Registra una unidad para poder crear turnos.</p>}
                <div className="package-selection"><div className="selection-heading"><h3>Paquetes disponibles</h3><span>{selectedPackages.length} seleccionados · {selectedWeight.toFixed(2)} kg</span></div>
                    {isLoading ? <p className="form-hint">Cargando paquetes...</p> : options.paquetesDisponibles.length === 0 ? <EmptyState title="No hay paquetes disponibles" description="Los paquetes asignados a una ruta activa no aparecen aquí." /> : <div className="package-check-list">{options.paquetesDisponibles.map((item) => <label className="package-check" key={item.idPaquete}><input type="checkbox" checked={selectedPackages.includes(item.idPaquete)} onChange={() => togglePackage(item.idPaquete)} /><span><strong>{item.folio}{item.esPrioritario ? ' · Prioritario' : ''}</strong><small>{item.direccionDestino} · {item.pesoKg} kg</small></span></label>)}</div>}
                </div>
                <div className="form-actions"><button className="primary-button" disabled={isSaving || options.unidades.length === 0 || selectedPackages.length === 0} type="submit">{isSaving ? 'Guardando...' : 'Asignar ruta'}</button></div>
            </form>

            <div className="section-heading"><h2>Monitoreo de rutas</h2><p>Información actualizada directamente desde TURNOS_REPARTIDOR y PARADAS_RUTA.</p></div>
            <div className="kpi-grid route-kpis"><article className="kpi-card blue"><span>Paquetes en ruta</span><strong>{monitoring.paquetesEnRuta}</strong><small>Asignados a turnos</small></article><article className="kpi-card orange"><span>Paradas pendientes</span><strong>{monitoring.paradasPendientes}</strong><small>Requieren seguimiento</small></article><article className="kpi-card red"><span>Incidencias</span><strong>{monitoring.incidenciasAbiertas}</strong><small>Relacionadas con rutas</small></article></div>
             {monitoring.turnos.length === 0 ? <section className="panel"><EmptyState title="No hay rutas asignadas" description="Las rutas guardadas aparecerán aquí para su monitoreo." /></section> : <section className="panel table-panel route-monitoring"><div className="table-wrapper"><table><thead><tr><th>Turno</th><th>Fecha</th><th>Repartidor</th><th>Unidad</th><th>Paquetes</th><th>Pendientes</th><th>Estado</th></tr></thead><tbody>{monitoring.turnos.map((turn) => <tr key={turn.idTurno}><td>#{turn.idTurno}</td><td>{turn.fechaTurno}</td><td>{turn.repartidor}</td><td>{turn.unidad}</td><td>{turn.totalPaquetes}</td><td>{turn.paradasPendientes}</td><td><span className="route-status">{turn.estadoTurno}</span></td></tr>)}</tbody></table></div>{monitoring.turnos.map((turn) => <div className="route-stops" key={`stops-${turn.idTurno}`}><strong>Turno #{turn.idTurno}</strong>{turn.paradas.map((stop) => <span key={stop.idParada}>{stop.ordenSecuencia}. {stop.folio} · {stop.estadoParada} · {stop.ultimoEvento}</span>)}</div>)}{displayedTurn?.geometria && <RouteMap routeGeometry={displayedTurn.geometria} height={360} />}</section>}
        </section>
    </>;
}
export default RoutesPage;
