const navigationItems = [
    { id: 'summary', label: 'Resumen', icon: '▦' },
    { id: 'routes', label: 'Rutas', icon: '⌁' },
    { id: 'maps', label: 'Mapa', icon: '⌖' },
    { id: 'packages', label: 'Paquetes', icon: '▣' },
    { id: 'incidents', label: 'Incidencias', icon: '⚑' },
    { id: 'drivers', label: 'Repartidores', icon: '♙' },
    { id: 'reports', label: 'Reportes', icon: '▤' },
    { id: 'centers', label: 'Centros', icon: '⌖' },
    { id: 'users', label: 'Usuarios', icon: '♙' },
];

function Sidebar({ activeModule, onNavigate, packagesInRoute, user, onLogout }) {
    return (
        <aside className="sidebar">
            <div className="brand-block">
                <span className="brand-name">RumboEnvíos</span>
                <span className="brand-caption">CONTROL TOWER</span>
            </div>
            <nav className="sidebar-nav" aria-label="Navegación principal">
                <span className="nav-section-label">OPERACIÓN</span>
                {navigationItems.map((item) => (
                    <button
                        className={`nav-item ${activeModule === item.id ? 'active' : ''}`}
                        key={item.id}
                        onClick={() => onNavigate(item.id)}
                        type="button"
                    >
                        <span className="nav-icon" aria-hidden="true">{item.icon}</span>
                        <span>{item.label}</span>
                        {item.id === 'summary' && <strong className="nav-badge">{packagesInRoute}</strong>}
                    </button>
                ))}
            </nav>
            <div className="sidebar-user">
                <span>{user?.nombre}</span>
                <small>Administrador</small>
                <button type="button" onClick={onLogout}>Cerrar sesión</button>
            </div>
        </aside>
    );
}

export default Sidebar;
