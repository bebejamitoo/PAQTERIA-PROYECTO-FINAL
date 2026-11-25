const HOP_BY_HOP_HEADERS = new Set([
    'connection',
    'content-length',
    'host',
    'keep-alive',
    'proxy-authenticate',
    'proxy-authorization',
    'te',
    'trailer',
    'transfer-encoding',
    'upgrade',
]);

export const config = {
    api: {
        bodyParser: false,
    },
};

async function readRequestBody(request) {
    const chunks = [];
    for await (const chunk of request) {
        chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
    }
    return Buffer.concat(chunks);
}

export default async function proxyApi(request, response) {
    const configuredOrigin = process.env.PAQTERIA_API_ORIGIN;
    if (!configuredOrigin) {
        response.status(503).json({
            message: 'La API de Paqteria aún no está configurada en este despliegue.',
        });
        return;
    }

    let upstream;
    try {
        const origin = new URL(configuredOrigin);
        const isLocalDevelopmentOrigin = ['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname);
        if (origin.protocol !== 'https:' && !isLocalDevelopmentOrigin) {
            throw new Error('La API remota debe usar HTTPS.');
        }

        const incoming = new URL(request.url || '/api/proxy', 'https://vercel.invalid');
        const apiPath = incoming.searchParams.get('path')?.replace(/^\/+/, '');
        if (!apiPath) {
            response.status(400).json({ message: 'Falta la ruta de la API solicitada.' });
            return;
        }

        upstream = new URL(`/api/${apiPath}`, origin);
        incoming.searchParams.delete('path');
        for (const [name, value] of incoming.searchParams) {
            upstream.searchParams.append(name, value);
        }
    } catch {
        response.status(500).json({
            message: 'La URL de la API configurada para este despliegue no es válida.',
        });
        return;
    }

    const headers = new Headers();
    for (const [name, value] of Object.entries(request.headers)) {
        if (value !== undefined && !HOP_BY_HOP_HEADERS.has(name.toLowerCase())) {
            headers.set(name, Array.isArray(value) ? value.join(', ') : value);
        }
    }
    headers.delete('accept-encoding');

    const method = request.method || 'GET';
    const hasBody = method !== 'GET' && method !== 'HEAD';

    try {
        const upstreamResponse = await fetch(upstream, {
            method,
            headers,
            body: hasBody ? await readRequestBody(request) : undefined,
            redirect: 'manual',
        });

        response.status(upstreamResponse.status);
        for (const [name, value] of upstreamResponse.headers) {
            const normalizedName = name.toLowerCase();
            if (
                normalizedName !== 'set-cookie'
                && !HOP_BY_HOP_HEADERS.has(normalizedName)
                && normalizedName !== 'content-encoding'
            ) {
                response.setHeader(name, value);
            }
        }

        const cookies = upstreamResponse.headers.getSetCookie?.() ?? [];
        if (cookies.length > 0) {
            response.setHeader('set-cookie', cookies);
        }

        if (method === 'HEAD' || upstreamResponse.status === 204 || upstreamResponse.status === 304) {
            response.end();
            return;
        }

        response.end(Buffer.from(await upstreamResponse.arrayBuffer()));
    } catch {
        response.status(502).json({
            message: 'No fue posible conectar con la API de Paqteria.',
        });
    }
}
