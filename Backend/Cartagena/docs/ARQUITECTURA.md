# Arquitectura de Cartagena

```text
REST Client / Scalar / Postman
       |
       v
NGINX Gateway localhost:8081
       |-------- /api/v1/* --------------> Login Node.js :3000
       |-------- /api/proveedores --------> Proveedores Flask :5000
       |-------- /api/resenas ------------> Reseñas Node.js :3000
       |-------- /api/historial-compras --> Historial Flask :5000
       |-------- /api/* ------------------> Cartagena.Api C# .NET 10 :8080
                                               |
                                               v
                                   MySQL 8.4: tienda_online
                                   (external: libreria-network)
```

La API C# maneja: `LibrosController`, `ClientesController`, `VentasController`, `PedidosController`, `PaquetesController`, `LogisticaController`, `AbastecimientoController`, `CuentasController`, `DigitalController` y `SistemaController`. Usa `DbFactory` para conexiones SQL con Dapper, políticas de autenticación JWT contra el token emitido por Login, y `ApiExceptionHandler` para respuestas JSON de error.

## Responsabilidades y consistencia

- **Ventas/pedidos:** una única tabla `venta` y detalle en `detalle_venta`; `/api/pedidos` es una vista de negocio, no otra tabla.
- **Stock:** el checkout bloquea filas de libros y decrementa stock dentro de una transacción; cancelación válida restituye stock una sola vez.
- **Pagos:** sandbox o confirmación administrativa de transferencia; no integración bancaria real.
- **Empaquetado:** `paquete` se asocia 1:1 con `venta` y modifica el estado de `envio`.
- **Seguridad:** Login firma HS256, C# valida firma/issuer/exp, los controllers protegidos exigen JWT y políticas de rol; usuarios nunca eligen IDs de otros usuarios para acceder a datos.
- **PDF:** archivos digital en volumen privado; comprador debe haber pagado para descargarlo.
- **SMTP:** opcional; sin SMTP no se envían correos. El sistema no incluye frontend.

## Endpoints y contratos

El archivo `docs/PETICIONES.http` presenta un recorrido de extremo a extremo. El esquema de `Cartagena.Api` se publica en `/openapi/v1.json`; Scalar en `/scalar/v1`.
