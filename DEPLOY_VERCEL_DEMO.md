# Demo gratuita en Vercel sin cambiar la conexión móvil

Esta configuración publica la aplicación web en Vercel y reenvía sus solicitudes
`/api/*` a la API que ya se ejecuta en el equipo local. La API sigue usando su
configuración y base de datos actuales. La app móvil no necesita cambios.

## Configurar Vercel

1. Importar el repositorio en Vercel y crear el proyecto con **Root Directory**
   `paqteria.client`.
2. Usar `npm run build` como comando de compilación y `dist` como directorio de
   salida. Vercel instalará dependencias con `npm install`/`npm ci`.
3. Agregar la variable de entorno de producción `PAQTERIA_API_ORIGIN`. Su valor
   se obtiene en el paso siguiente.

La función `paqteria.client/api/[...path].js` reenvía solicitudes y cookies de
sesión desde el mismo dominio de Vercel, por lo que la web conserva las rutas
relativas `/api/...` y el login por cookie.

## Publicar la API local temporalmente

1. Mantener la API actualizada accesible en `http://localhost:5273`. Se usa
   este puerto para no tocar el puerto `5272` que ya utiliza la app móvil.
2. Instalar `cloudflared` y ejecutar:

   ```powershell
   cloudflared tunnel --url http://localhost:5273
   ```

3. Copiar el dominio HTTPS `https://....trycloudflare.com` que muestra el
   comando y guardarlo como `PAQTERIA_API_ORIGIN` en Vercel, sin agregar `/api`.
4. Volver a desplegar el proyecto de Vercel.
5. Mantener encendidos la PC, la API y el proceso `cloudflared`.

Si el túnel se detiene, el dominio Quick Tunnel puede cambiar. En ese caso hay
que actualizar `PAQTERIA_API_ORIGIN` en Vercel y desplegar de nuevo. Esto es para
demostración, no para un servicio con disponibilidad garantizada.

## La app móvil

La app móvil continúa usando su dirección actual y la misma API/base de datos.
Para acceso desde fuera de la red local podría usar el dominio del túnel
directamente, pero eso no es necesario para publicar la web y no se cambia aquí.

No guardar contraseñas, cadenas de conexión ni el dominio del túnel en el
repositorio. Configurar `PAQTERIA_API_ORIGIN` solo en las variables del proyecto
de Vercel.
