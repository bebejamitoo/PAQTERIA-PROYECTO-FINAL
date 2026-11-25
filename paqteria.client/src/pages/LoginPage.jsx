import { useState } from 'react';

function LoginPage({ onLogin }) {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState(null);

    async function handleSubmit(event) {
        event.preventDefault();
        if (isSaving) return;

        setIsSaving(true);
        setError(null);
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password }),
            });
            const body = await response.json();
            if (!response.ok) throw new Error(body.message ?? 'No fue posible iniciar sesión.');
            onLogin(body);
        } catch (loginError) {
            setError(loginError.message);
        } finally {
            setIsSaving(false);
        }
    }

    return (
        <main className="login-shell">
            <section className="login-card">
                <div className="brand-block login-brand">
                    <img className="brand-logo login-logo" src="/logo.png" alt="PaqTracker" />
                </div>
                <div className="login-heading">
                    <h1>Acceso administrativo</h1>
                    <p>Inicia sesión para gestionar la operación de PAQTERIA.</p>
                </div>
                {error && <div className="operation-message error">{error}</div>}
                <form className="login-form" onSubmit={handleSubmit}>
                    <label>Correo electrónico<input type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" required /></label>
                    <label>Contraseña<input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" required /></label>
                    <button className="primary-button" disabled={isSaving} type="submit">{isSaving ? 'Validando...' : 'Iniciar sesión'}</button>
                </form>
            </section>
        </main>
    );
}

export default LoginPage;
