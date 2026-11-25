import { useEffect, useMemo, useRef, useState } from 'react';
import { BrowserQRCodeReader } from '@zxing/browser';
import EmptyState from '../components/EmptyState';
import { PageHeader } from './SummaryPage';

const emptyForm = {
    folio: '',
    idCentroOrigen: 0,
    direccionOrigen: '',
    direccionDestino: '',
    coordenadasDestino: '',
    pesoKg: 0,
    tamanoEtiqueta: '',
    esPrioritario: false,
    esFragil: false,
};

const fieldAliases = {
    folio: ['folio', 'codigo', 'código', 'tracking', 'idpaquete', 'id'],
    idCentroOrigen: ['idcentroorigen', 'centroorigen', 'centro', 'centro origen'],
    direccionOrigen: ['direccionorigen', 'direcciónorigen', 'origen', 'direccion de origen', 'dirección de origen'],
    direccionDestino: ['direcciondestino', 'direccióndestino', 'destino', 'direccion de destino', 'dirección de destino'],
    coordenadasDestino: ['coordenadasdestino', 'coordenadas', 'coordenadas destino', 'ubicacion', 'ubicación'],
    pesoKg: ['pesokg', 'peso', 'peso kg', 'weight'],
    tamanoEtiqueta: ['tamanoetiqueta', 'tamañoetiqueta', 'tamano', 'tamaño', 'etiqueta'],
    esPrioritario: ['esprioritario', 'prioritario', 'prioridad'],
    esFragil: ['esfragil', 'esfrágil', 'fragil', 'frágil'],
};

function normalizeKey(value) {
    return String(value).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[\s_-]/g, '');
}

function parseQrPayload(rawValue) {
    const value = rawValue.trim();
    let source;

    try {
        source = JSON.parse(value);
        if (source && typeof source === 'object' && !Array.isArray(source)) {
            source = source.paquete ?? source.package ?? source.data ?? source;
        }
    } catch {
        try {
            if (!value.includes('?') && !value.includes('=')) return { folio: value };
            const query = value.includes('?') ? value.slice(value.indexOf('?') + 1) : value;
            source = Object.fromEntries(new URLSearchParams(query));
        } catch {
            source = null;
        }
    }

    if (!source || typeof source !== 'object' || Array.isArray(source)) {
        return { folio: value };
    }

    const parsed = {};
    for (const [field, aliases] of Object.entries(fieldAliases)) {
        const entry = Object.entries(source).find(([key]) => aliases.some((alias) => normalizeKey(alias) === normalizeKey(key)));
        if (entry) parsed[field] = entry[1];
    }

    return {
        ...parsed,
        idCentroOrigen: parsed.idCentroOrigen === undefined ? undefined : Number(parsed.idCentroOrigen),
        pesoKg: parsed.pesoKg === undefined ? undefined : Number(parsed.pesoKg),
        esPrioritario: parsed.esPrioritario === undefined ? undefined : parseBoolean(parsed.esPrioritario),
        esFragil: parsed.esFragil === undefined ? undefined : parseBoolean(parsed.esFragil),
    };
}

function parseBoolean(value) {
    return value === true || ['true', '1', 'si', 'sí', 'yes'].includes(String(value).toLowerCase());
}

function PackagesPage() {
    const [search, setSearch] = useState('');
    const [scanFolio, setScanFolio] = useState('');
    const [packages, setPackages] = useState([]);
    const [form, setForm] = useState(emptyForm);
    const [isFormOpen, setIsFormOpen] = useState(false);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [isQrScannerOpen, setIsQrScannerOpen] = useState(false);
    const [message, setMessage] = useState(null);
    const scanInputRef = useRef(null);
    const qrVideoRef = useRef(null);
    const qrControlsRef = useRef(null);

    useEffect(() => {
        loadPackages();
    }, []);

    useEffect(() => {
        if (!isQrScannerOpen) return undefined;

        const reader = new BrowserQRCodeReader();
        let isMounted = true;
        reader.decodeFromConstraints(
            { video: { facingMode: { ideal: 'environment' } } },
            qrVideoRef.current,
            (result) => {
                if (!isMounted || !result) return;
                const scannedForm = parseQrPayload(result.getText());
                setForm((current) => ({ ...current, ...Object.fromEntries(Object.entries(scannedForm).filter(([, value]) => value !== undefined && value !== '')) }));
                setIsQrScannerOpen(false);
                setMessage({ type: 'success', text: 'QR leído correctamente. Revisa los datos y guarda el paquete.' });
            },
        ).then((controls) => {
            if (isMounted) qrControlsRef.current = controls;
            else controls.stop();
        }).catch(() => {
            if (isMounted) setMessage({ type: 'error', text: 'No fue posible acceder a la cámara. Comprueba los permisos del navegador.' });
        });

        return () => {
            isMounted = false;
            qrControlsRef.current?.stop();
            qrControlsRef.current = null;
        };
    }, [isQrScannerOpen]);

    async function loadPackages() {
        setIsLoading(true);
        try {
            const response = await fetch('/api/packages');
            if (!response.ok) throw new Error('No fue posible consultar los paquetes.');
            setPackages(await response.json());
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally {
            setIsLoading(false);
        }
    }

    function handleFormChange(event) {
        const { name, type, value, checked } = event.target;
        setForm((current) => ({
            ...current,
            [name]: type === 'checkbox' ? checked : type === 'number' ? Number(value) : value,
        }));
    }

    async function handleCreate(event) {
        event.preventDefault();
        if (isSaving) return;

        setIsSaving(true);
        setMessage(null);
        try {
            const coordinateParts = form.coordenadasDestino.split(',').map((part) => Number(part.trim()));
            if (coordinateParts.length !== 2
                || !coordinateParts.every(Number.isFinite)
                || coordinateParts[0] < 14 || coordinateParts[0] > 33.5
                || coordinateParts[1] < -118.5 || coordinateParts[1] > -86.5) {
                throw new Error('Las coordenadas deben estar en México y usar el formato latitud, longitud; por ejemplo: 29.0729,-110.9559.');
            }

            const response = await fetch('/api/packages', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(form),
            });
            const body = await response.json();
            if (!response.ok) throw new Error(body.message ?? 'No fue posible registrar el paquete.');

            setForm(emptyForm);
            setIsFormOpen(false);
            setMessage({ type: 'success', text: `Paquete ${body.folio} registrado correctamente en la base de datos.` });
            await loadPackages();
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally {
            setIsSaving(false);
        }
    }

    async function handleEnter(event) {
        event.preventDefault();
        const folio = scanFolio.trim();
        if (!folio || isSaving) return;

        setIsSaving(true);
        setMessage(null);
        try {
            const response = await fetch(`/api/packages/${encodeURIComponent(folio)}/enter`, { method: 'POST' });
            const body = await response.json();
            if (!response.ok) throw new Error(body.message ?? 'No fue posible ingresar el paquete.');
            setScanFolio('');
            setMessage({ type: 'success', text: `Paquete ${body.folio} ingresado correctamente.` });
            await loadPackages();
            scanInputRef.current?.focus();
        } catch (error) {
            setMessage({ type: 'error', text: error.message });
        } finally {
            setIsSaving(false);
        }
    }

    const visiblePackages = useMemo(() => {
        const normalizedSearch = search.trim().toLowerCase();
        if (!normalizedSearch) return packages;
        return packages.filter((item) => [item.folio, item.idCentroOrigen, item.direccionDestino, item.estadoActual]
            .some((value) => String(value ?? '').toLowerCase().includes(normalizedSearch)));
    }, [packages, search]);

    return <>
        <PageHeader
            title="Gestión de paquetes"
            subtitle="Consulta el historial, estado y trazabilidad de cada envío."
            action={<button className="primary-button" onClick={() => setIsFormOpen(true)} type="button">+ Registrar paquete</button>}
        />
        <section className="content-section">
            {isFormOpen && <form className="package-form" onSubmit={handleCreate}>
                <div className="form-heading"><div><h2>Registrar paquete</h2><p>Completa la información para crear el registro en PAQTERIA.</p></div><button className="form-close" onClick={() => setIsFormOpen(false)} type="button" aria-label="Cerrar formulario">×</button></div>
                <div className="form-grid">
                    <label>Folio<div className="qr-field"><input name="folio" value={form.folio} onChange={handleFormChange} required /><button className="scan-button" type="button" onClick={() => setIsQrScannerOpen(true)}>Escanear QR</button></div></label>
                    <label>ID centro origen<input name="idCentroOrigen" type="number" min="1" value={form.idCentroOrigen || ''} onChange={handleFormChange} required /></label>
                    <label>Peso (kg)<input name="pesoKg" type="number" min="0.01" step="0.01" value={form.pesoKg || ''} onChange={handleFormChange} required /></label>
                    <label>Tamaño de etiqueta<input name="tamanoEtiqueta" value={form.tamanoEtiqueta} onChange={handleFormChange} placeholder="Ej. mediana" required /></label>
                    <label>Coordenadas destino<input name="coordenadasDestino" value={form.coordenadasDestino} onChange={handleFormChange} placeholder="Latitud, longitud" required /></label>
                    <label className="form-field-wide">Dirección de origen<textarea name="direccionOrigen" value={form.direccionOrigen} onChange={handleFormChange} required /></label>
                    <label className="form-field-wide">Dirección de destino<textarea name="direccionDestino" value={form.direccionDestino} onChange={handleFormChange} required /></label>
                </div>
                <div className="check-options"><label><input name="esPrioritario" type="checkbox" checked={form.esPrioritario} onChange={handleFormChange} /> Envío prioritario</label><label><input name="esFragil" type="checkbox" checked={form.esFragil} onChange={handleFormChange} /> Paquete frágil</label></div>
                <div className="form-actions"><button className="secondary-button" onClick={() => setIsFormOpen(false)} type="button">Cancelar</button><button className="primary-button" disabled={isSaving} type="submit">{isSaving ? 'Guardando...' : 'Guardar paquete'}</button></div>
            </form>}
            {isQrScannerOpen && <div className="qr-modal-backdrop" role="presentation" onClick={(event) => event.target === event.currentTarget && setIsQrScannerOpen(false)}>
                <section className="qr-modal" role="dialog" aria-modal="true" aria-labelledby="qr-title">
                    <div className="form-heading"><div><h2 id="qr-title">Escanear código QR</h2><p>Coloca el código dentro del recuadro para cargar los datos.</p></div><button className="form-close" onClick={() => setIsQrScannerOpen(false)} type="button" aria-label="Cerrar escáner">×</button></div>
                    <div className="qr-viewfinder"><video ref={qrVideoRef} autoPlay muted playsInline /></div>
                    <p className="qr-help">El navegador solicitará permiso para usar la cámara.</p>
                    <button className="secondary-button qr-cancel" onClick={() => setIsQrScannerOpen(false)} type="button">Cancelar</button>
                </section>
            </div>}
            <form className="warehouse-entry" onSubmit={handleEnter}>
                <div><h2>Ingreso a almacén</h2><p>Escanea un folio ya registrado para cambiarlo a clasificación.</p></div>
                <div className="entry-controls"><input ref={scanInputRef} value={scanFolio} onChange={(event) => setScanFolio(event.target.value)} placeholder="Escanear o escribir folio" aria-label="Folio del paquete" /><button className="primary-button" disabled={isSaving || scanFolio.trim().length === 0} type="submit">{isSaving ? 'Guardando...' : 'Ingresar'}</button></div>
            </form>
            {message && <div className={`operation-message ${message.type}`} role="status">{message.text}</div>}
            <div className="search-box"><span>⌕</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar por folio o destino" aria-label="Buscar paquetes" /></div>
            <section className="panel table-panel"><div className="table-wrapper"><table><thead><tr><th>Folio</th><th>Centro origen</th><th>Estado</th><th>Fecha de creación</th><th /></tr></thead><tbody>{visiblePackages.map((item) => <tr key={item.idPaquete}><td>{item.folio}</td><td>{item.idCentroOrigen}</td><td><span className="package-status">{item.estadoActual}</span></td><td>{new Date(item.fechaCreacion).toLocaleString('es-MX')}</td><td><button className="detail-button" type="button">Ver detalle →</button></td></tr>)}</tbody></table></div>{isLoading ? <EmptyState title="Cargando paquetes" description="Consultando la información de la API." /> : visiblePackages.length === 0 && <EmptyState title="Sin paquetes disponibles" description={search ? 'No se encontraron paquetes con esa búsqueda.' : 'Los paquetes registrados aparecerán en esta tabla.'} />}</section>
        </section>
    </>;
}

export default PackagesPage;