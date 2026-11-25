import { useEffect, useState } from 'react';
import EmptyState from '../components/EmptyState';
import { PageHeader } from './SummaryPage';

const emptyForm = {
    tipoIncidencia: '',
    severidad: 'Media',
    idPaquete: '',
    idRepartidor: '',
    comentario: '',
};

async function readResponseBody(response) {
    const text = await response.text();
    if (!text.trim()) return null;
    try {
        return JSON.parse(text);
    } catch {
        return { message: text };
    }
}

function IncidentsPage() {
    const [incidents, setIncidents] = useState([]);
    const [packages, setPackages] = useState([]);
    const [drivers, setDrivers] = useState([]);
    const [form, setForm] = useState(emptyForm);
    const [selectedIncident, setSelectedIncident] = useState(null);
    const [isFormOpen, setIsFormOpen] = useState(false);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [message, setMessage] = useState(null);

    useEffect(() => {
        loadData();
    }, []);

    async function loadData() {
        setIsLoading(true);
        try {
            const [incidentsResponse, optionsResponse] = await Promise.all([
                fetch('/api/incidents'),
                fetch('/api/incidents/options'),
            ]);
            const incidentsBody = await readResponseBody(incidentsResponse);
            const optionsBody = await readResponseBody(optionsResponse);
            if (!incidentsResponse.ok) throw new Error(incidentsBody?.message ?? 'No fue posible consultar las incidencias.');
            if (!optionsResponse.ok) throw new Error(optionsBody?.message ?? 'No fue posible cargar las opciones.');
            setIncidents(Array.isArray(incidentsBody) ? incidentsBody : []);
            setPackages(optionsBody?.paquetes ?? []);
            setDrivers(optionsBody?.repartidores ?? []);
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally {
            setIsLoading(false);
        }
    }

    function handleChange(event) {
        const { name, value } = event.target;
        setForm((current) => ({ ...current, [name]: value }));
    }

    async function handleCreate(event) {
        event.preventDefault();
        if (isSaving) return;

        setIsSaving(true);
        setMessage(null);
        try {
            const response = await fetch('/api/incidents', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    idPaquete: Number(form.idPaquete),
                    idRepartidor: Number(form.idRepartidor),
                    tipoIncidencia: form.tipoIncidencia,
                    comentario: form.comentario,
                    urlFotoReporte: null,
                }),
            });
            const body = await readResponseBody(response);
            if (!response.ok) throw new Error(body?.message ?? `No fue posible registrar la incidencia (${response.status}).`);
            if (!body) throw new Error('La API no devolvió la incidencia creada. Reinicia el servidor e inténtalo de nuevo.');

            setForm(emptyForm);
            setIsFormOpen(false);
            setSelectedIncident(body);
            setMessage({ type: 'success', text: `Se registró la incidencia «${body.tipoIncidencia}».` });
            await loadData();
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally {
            setIsSaving(false);
        }
    }

    return <>
        <PageHeader
            title="Control de excepciones"
            subtitle="Incidencias de entrega y seguimiento almacenadas en SQL Server."
            action={<div className="header-actions"><span className="live-status"><i /> En vivo</span><button className="primary-button" onClick={() => { setIsFormOpen(true); setMessage(null); }} type="button">+ Registrar incidencia</button></div>}
        />
        <section className="content-section incidents-section">
            {isFormOpen && <form className="incident-form" onSubmit={handleCreate}>
                <div className="form-heading"><div><h2>Nueva incidencia</h2><p>La tabla registrará el paquete, repartidor si está asignado y detalle de la incidencia.</p></div><button className="form-close" onClick={() => setIsFormOpen(false)} type="button" aria-label="Cerrar formulario">×</button></div>
                <div className="incident-form-grid">
                    <label className="incident-description">Descripción breve<input name="tipoIncidencia" value={form.tipoIncidencia} onChange={handleChange} placeholder="Ej. Domicilio incompleto" required /></label>
                    <label>Severidad<select name="severidad" value={form.severidad} onChange={handleChange}><option>Baja</option><option>Media</option><option>Alta</option></select></label>
                    <label>Paquete<select name="idPaquete" value={form.idPaquete} onChange={handleChange} required><option value="">Selecciona un paquete</option>{packages.map((item) => <option key={item.idPaquete} value={item.idPaquete}>{item.folio} · {item.direccionDestino}</option>)}</select></label>
                    <label>Repartidor<select name="idRepartidor" value={form.idRepartidor} onChange={handleChange} required><option value="">Selecciona un repartidor</option>{drivers.map((item) => <option key={item.idRepartidor} value={item.idRepartidor}>{item.nombre}</option>)}</select></label>
                    <label className="incident-comment">Detalle<textarea name="comentario" value={form.comentario} onChange={handleChange} placeholder="Agrega contexto para el equipo (opcional)" /></label>
                </div>
                <div className="form-actions"><button className="secondary-button" onClick={() => setIsFormOpen(false)} type="button">Cancelar</button><button className="primary-button" disabled={isSaving} type="submit">{isSaving ? 'Guardando...' : 'Guardar incidencia'}</button></div>
            </form>}
            {message && <div className={`operation-message ${message.type}`} role="status">{message.text}</div>}
            <div className="split-grid incidents-grid">
                <section className="panel incident-inbox"><div className="panel-title"><h2>Bandeja de incidencias</h2><p>Las incidencias registradas aparecen primero.</p></div>{isLoading ? <EmptyState title="Cargando incidencias" description="Consultando los registros en SQL Server." /> : incidents.length === 0 ? <EmptyState title="No hay incidencias registradas" description="Las nuevas incidencias aparecerán en esta bandeja." /> : <div className="incident-list">{incidents.map((incident) => <button className={`incident-item ${selectedIncident?.idIncidencia === incident.idIncidencia ? 'selected' : ''}`} key={incident.idIncidencia} onClick={() => setSelectedIncident(incident)} type="button"><span className="incident-dot" /><span className="incident-item-content"><strong>{incident.tipoIncidencia}</strong><small>{incident.repartidor || 'Sin repartidor asignado'} · {incident.folio}</small></span><span className="incident-arrow">›</span></button>)}</div>}</section>
                <section className="panel incident-detail"><div className="panel-title"><h2>Detalle de incidencia</h2></div>{selectedIncident === null ? <EmptyState title="Selecciona una incidencia" description="El detalle estará disponible al seleccionar un registro." /> : <div className="incident-detail-content"><span className="incident-severity">Media</span><h2>{selectedIncident.tipoIncidencia}</h2><p>{selectedIncident.comentario || 'Sin comentario adicional.'}</p><dl><div><dt>Paquete</dt><dd>{selectedIncident.folio}</dd></div><div><dt>Repartidor</dt><dd>{selectedIncident.repartidor || 'Sin repartidor asignado'}</dd></div><div><dt>Creada</dt><dd>{selectedIncident.fechaHora ? new Date(selectedIncident.fechaHora).toLocaleString('es-MX') : 'Sin fecha'}</dd></div><div><dt>Registro</dt><dd>#{selectedIncident.idIncidencia}</dd></div></dl></div>}</section>
            </div>
        </section>
    </>;
}

export default IncidentsPage;
