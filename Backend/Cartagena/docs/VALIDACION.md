# Validaciones y pendientes antes de entregar a tu profesor

## Qué se verificó al generar el proyecto

- Estructura del árbol y todos los archivos que consume Compose.
- Rutas de controladores: equivalencia de las **44 rutas Flask principales**, revisadas contra el código original.
- Archivo YAML de Compose, .env.example, referencias de Dockerfiles.
- Controladores protegidos, SQL parametrizado, `Idempotency-Key`, JWT y validación de rol en el código.

## Qué NO se verificó en este entorno

No se dispone de SDK .NET, motor Docker, ni instancia de tu MySQL. Por tanto, **no se afirma que ya compile o que haya pasado pruebas de integración**.

Para validar realmente en tu Arch Linux:

```bash
dotnet --info
dotnet restore Cartagena.slnx
dotnet build Cartagena.slnx -c Release
dotnet test Cartagena.slnx

docker compose config
docker compose build
docker compose up -d
docker compose ps
bash pruebas/comprobar.sh
```

### Pruebas manuales imprescindibles

1. GET `/api/core/health` y `/api/libros` deben devolver 200.
2. Registro/Login Node deben generar JWT, válido para C#.
3. POST `/api/libros` como ADMIN debe crear libro y como CLIENTE debe responder 403.
4. PUT `/api/carrito/{id}` y GET `/api/carrito` deben usar solo carrito del usuario autenticado.
5. POST `/api/checkout` con domicilio propio, stock y `Idempotency-Key` crea venta/detalle/envío e inventario.
6. Repetir checkout con la misma clave devuelve `repetido: true` y no modifica stock.
7. Cancelar venta pendiente repone stock; segunda cancelación da conflicto.
8. `/api/pedidos`, `/api/ventas` e historial remoto muestran los mismos IDs de venta.
9. Crear y actualizar paquete; verificar `envio` y prohibición de retroceso de estados.
10. Ticket PDF descarga y archivo PDF privado solo para comprador pagado/admin.
11. Proveedor, reseñas, login e historial siguen pasando sus endpoints externos.
12. `docker compose logs --tail=100 libreria-core` sin excepciones SQL.

### Límites conocidos

- Intercambio JWT por secreto compartido; debe estar protegido en `.env`.
- `CORS_ORIGIN` vacío por diseño porque no hay frontend.
- Tarjeta aprobada es simulación académica; no hay pasarela PCI.
- Restablecimiento/verificación requieren SMTP configurado para recibir tokens.
- Los endpoints no tienen pruebas de integración con una base MySQL real hasta que ejecutes los pasos anteriores.
- Si el proyecto anterior tiene PDFs almacenados en un volumen distinto, hay que migrar esos archivos por separado.
