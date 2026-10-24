#!/usr/bin/env bash
set -euo pipefail
BASE="${API_URL:-http://localhost:8081}"
failed=0
check() {
  local path="$1" expected="$2" got
  got=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$BASE$path") || got=000
  printf '%s %-47s esperado: %s\n' "$got" "$path" "$expected"
  [[ "$got" == "$expected" ]] || failed=1
}
check /health 200
check /api/core/health 200
check /api/v1/salud 200
check /api/libros 200
check /api/historial-compras/health 200
check /api/carrito 401
check /api/pedidos 401
check /api/paquetes 401
check /api/ventas 401
check /api/admin/resumen 401
check /api/historial-compras 401
check /api/v1/auth/verificar 401
check /openapi/v1.json 200
if (( failed == 0 )); then echo 'Pruebas básicas correctas.'; else echo 'Fallaron pruebas; revisa docker compose logs.'; fi
exit "$failed"
