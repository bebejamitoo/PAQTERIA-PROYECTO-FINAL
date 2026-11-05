import { useCallback, useEffect, useState } from 'react';
import './App.css';
import Sidebar from './components/Sidebar';
import SummaryPage from './pages/SummaryPage';
import RoutesPage from './pages/RoutesPage';
import MapsPage from './pages/MapsPage';
import PackagesPage from './pages/PackagesPage';
import IncidentsPage from './pages/IncidentsPage';
import DriversPage from './pages/DriversPage';
import ReportsPage from './pages/ReportsPage';
import CentersPage from './pages/CentersPage';
import UsersPage from './pages/UsersPage';
import LoginPage from './pages/LoginPage';

const emptyMonitoring = {
    turnos: [],
    paquetesEnRuta: 0,
    paradasPendientes: 0,
    incidenciasAbiertas: 0,
};

function App() {
    const [activeModule, setActiveModule] = useState('summary');
    const [user, setUser] = useState(null);
    const [isCheckingSession, setIsCheckingSession] = useState(true);
    const [monitoring, setMonitoring] = useState(emptyMonitoring);

    const refreshMonitoring = useCallback(async () => {
        const response = await fetch('/api/routes/monitoring');
        if (!response.ok) return;
        setMonitoring(await response.json());
    }, []);

    useEffect(() => {
        fetch('/api/auth/me')
            .then(async (response) => response.ok ? response.json() : null)
            .then(async (currentUser) => {
                setUser(currentUser);
                if (currentUser) await refreshMonitoring();
            })
            .finally(() => setIsCheckingSession(false));
    }, [refreshMonitoring]);

    async function handleLogout() {
        await fetch('/api/auth/logout', { method: 'POST' });
        setUser(null);
        setMonitoring(emptyMonitoring);
    }

    if (isCheckingSession) return <main className="login-shell"><p className="loading-message">Comprobando sesión...</p></main>;
    if (!user) return <LoginPage onLogin={async (loggedUser) => { setUser(loggedUser); await refreshMonitoring(); }} />;

    const pages = {
        summary: <SummaryPage monitoring={monitoring} />,
        routes: <RoutesPage onMonitoringChange={setMonitoring} />,
        maps: <MapsPage />,
        packages: <PackagesPage />,
        incidents: <IncidentsPage />,
        drivers: <DriversPage />,
        reports: <ReportsPage />,
        centers: <CentersPage />,
        users: <UsersPage />,
    };
    return (
        <div className="app-shell">
            <Sidebar activeModule={activeModule} onNavigate={setActiveModule} packagesInRoute={monitoring.paquetesEnRuta} user={user} onLogout={handleLogout} />
            <main className="main-content">
                {pages[activeModule]}
            </main>
        </div>
    );
}

export default App;