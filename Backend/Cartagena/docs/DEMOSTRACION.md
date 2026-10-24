# Guía de presentación de backend Cartagena

Abre `http://localhost:8081/scalar/v1` o `docs/PETICIONES.http` en VS Code.

1. **Arquitectura**: muestra `docker compose ps`, Gateway y la carpeta `backend/Cartagena.Api/Controllers`.
2. **Persistencia**: explica las 14 tablas existentes más migraciones, `DbFactory` y MySQL compartido.
3. **Autenticación**: usa `/api/v1/auth/login` del microservicio Node; pasa su JWT a la API C#.
4. **Catálogo/inventario**: consulta libros, como ADMIN agrega uno y consulta descuentos/stock.
5. **Venta**: crea domicilio propio, agrega libro al carrito, envía checkout con `Idempotency-Key`.
6. **Idempotencia**: repite la petición EXACTA; demuestra que no cambia stock y devuelve `repetido:true`.
7. **Pedidos**: consulta `api/pedidos` y comprueba que consulta `venta` real sin duplicar datos.
8. **Logística**: paquete, guía, envío y garantía después de la entrega.
9. **Documentación**: OpenAPI/Scalar y `dotnet test` (si compiló).
10. **Seguridad**: prueba un endpoint sin JWT (401), con CLIENTE para una ruta ADMIN (403), y evita mostrar claves privadas.

> No afirmes pagos bancarios reales ni correos automáticos si SMTP no está configurado.
