import os
import logging
from contextlib import contextmanager
from flask import Flask, jsonify, request
from flask_cors import CORS
import jwt
import mysql.connector
from mysql.connector import Error, IntegrityError
from dotenv import load_dotenv

load_dotenv(override=False)
app = Flask(__name__)
CORS(app, resources={r"/api/*": {"origins": os.getenv("CORS_ORIGIN", "http://localhost:5173").split(",")}})
logging.basicConfig(level=logging.INFO)

DB_CONFIG = {
    "host": os.getenv("DB_HOST", "mysql"),
    "port": int(os.getenv("DB_PORT", "3306")),
    "database": os.getenv("DB_NAME", "tienda_online"),
    "user": os.getenv("DB_USER", "usuario_app"),
    "password": os.getenv("DB_PASSWORD", ""),
}

@app.before_request
def proteger_administracion():
    if request.path == '/api/health' or request.method == 'OPTIONS':
        return None
    # Este servicio es un panel administrativo, incluido su listado de RFC.
    bearer = request.headers.get('Authorization','')
    secret = os.getenv('JWT_SECRET','')
    if len(secret)<32 or not bearer.startswith('Bearer '):
        return jsonify({'success':False,'message':'Falta token de administrador'}),401
    try:
        payload = jwt.decode(bearer[7:], secret, algorithms=['HS256'],
                             issuer=os.getenv('JWT_ISSUER','microservicio-login'),
                             options={'require':['exp','sub','iss']})
    except jwt.InvalidTokenError:
        return jsonify({'success':False,'message':'Token inválido'}),401
    if str(payload.get('rol','')).upper() != 'ADMIN':
        return jsonify({'success':False,'message':'Requiere rol ADMIN'}),403

CAMPOS = ("nombre", "contacto", "razon_social", "rfc", "direccion", "correo", "telefono")
SELECT_PROVEEDOR = """
    SELECT id_proveedor AS id, nombre, contacto, razon_social, rfc,
           direccion, correo, telefono
    FROM proveedor
"""

@contextmanager
def conexion():
    conn = mysql.connector.connect(**DB_CONFIG)
    try:
        yield conn
    finally:
        conn.close()


def validar_proveedor(data, parcial=False):
    if not isinstance(data, dict):
        return "El cuerpo debe ser un objeto JSON"
    if not parcial and not isinstance(data.get("nombre"), str):
        return "El campo 'nombre' es obligatorio y debe ser texto"
    if "nombre" in data and (not isinstance(data["nombre"], str) or not data["nombre"].strip()):
        return "El campo 'nombre' no puede estar vacío"
    for campo in CAMPOS:
        if campo in data and data[campo] is not None and not isinstance(data[campo], str):
            return f"El campo '{campo}' debe ser texto"
    if not parcial and any(k not in data for k in ("razon_social", "rfc", "direccion", "correo", "telefono")):
        return "Faltan campos obligatorios: razon_social, rfc, direccion, correo o telefono"
    if parcial and not any(c in data for c in CAMPOS):
        return "No hay campos válidos para actualizar"
    return None


def error_bd(e):
    app.logger.exception("Error de base de datos")
    if getattr(e, "errno", None) == 1062:
        return jsonify({"success": False, "message": "Ya existe un registro con ese valor único"}), 409
    if getattr(e, "errno", None) == 1451:
        return jsonify({"success": False, "message": "No se puede eliminar: existen registros relacionados"}), 409
    return jsonify({"success": False, "message": "Error interno de base de datos"}), 500


@app.get("/api/proveedores")
def listar_proveedores():
    try:
        with conexion() as conn:
            with conn.cursor(dictionary=True) as cursor:
                cursor.execute(SELECT_PROVEEDOR + " ORDER BY id_proveedor DESC")
                return jsonify({"success": True, "data": cursor.fetchall()}), 200
    except Error as e:
        return error_bd(e)


@app.get("/api/proveedores/<int:id>")
def obtener_proveedor(id):
    try:
        with conexion() as conn:
            with conn.cursor(dictionary=True) as cursor:
                cursor.execute(SELECT_PROVEEDOR + " WHERE id_proveedor = %s", (id,))
                proveedor = cursor.fetchone()
                if proveedor is None:
                    return jsonify({"success": False, "message": "Proveedor no encontrado"}), 404
                return jsonify({"success": True, "data": proveedor}), 200
    except Error as e:
        return error_bd(e)


@app.post("/api/proveedores")
def crear_proveedor():
    data = request.get_json(silent=True)
    error = validar_proveedor(data)
    if error:
        return jsonify({"success": False, "message": error}), 400
    try:
        with conexion() as conn:
            with conn.cursor(dictionary=True) as cursor:
                if data.get("rfc"):
                    cursor.execute("SELECT id_proveedor FROM proveedor WHERE rfc = %s", (data["rfc"],))
                    if cursor.fetchone():
                        return jsonify({"success": False, "message": "Ya existe un proveedor con ese RFC"}), 409
                cursor.execute("""
                    INSERT INTO proveedor (nombre, contacto, razon_social, rfc, direccion, correo, telefono)
                    VALUES (%s, %s, %s, %s, %s, %s, %s)
                """, tuple(data.get(c) for c in CAMPOS))
                nuevo_id = cursor.lastrowid
                conn.commit()
                cursor.execute(SELECT_PROVEEDOR + " WHERE id_proveedor = %s", (nuevo_id,))
                return jsonify({"success": True, "message": "Proveedor creado correctamente", "data": cursor.fetchone()}), 201
    except Error as e:
        return error_bd(e)


@app.put("/api/proveedores/<int:id>")
def actualizar_proveedor(id):
    data = request.get_json(silent=True)
    error = validar_proveedor(data, parcial=True)
    if error:
        return jsonify({"success": False, "message": error}), 400
    campos = [campo for campo in CAMPOS if campo in data]
    try:
        with conexion() as conn:
            with conn.cursor(dictionary=True) as cursor:
                cursor.execute("SELECT id_proveedor FROM proveedor WHERE id_proveedor = %s", (id,))
                if cursor.fetchone() is None:
                    return jsonify({"success": False, "message": "Proveedor no encontrado"}), 404
                if "rfc" in data and data["rfc"]:
                    cursor.execute("SELECT id_proveedor FROM proveedor WHERE rfc = %s AND id_proveedor <> %s", (data["rfc"], id))
                    if cursor.fetchone():
                        return jsonify({"success": False, "message": "El RFC ya pertenece a otro proveedor"}), 409
                set_clause = ", ".join(f"`{campo}` = %s" for campo in campos)
                cursor.execute(f"UPDATE proveedor SET {set_clause} WHERE id_proveedor = %s", tuple(data[c] for c in campos) + (id,))
                conn.commit()
                cursor.execute(SELECT_PROVEEDOR + " WHERE id_proveedor = %s", (id,))
                return jsonify({"success": True, "message": "Proveedor actualizado correctamente", "data": cursor.fetchone()}), 200
    except Error as e:
        return error_bd(e)


@app.delete("/api/proveedores/<int:id>")
def eliminar_proveedor(id):
    try:
        with conexion() as conn:
            with conn.cursor(dictionary=True) as cursor:
                cursor.execute("SELECT id_proveedor FROM proveedor WHERE id_proveedor = %s FOR UPDATE", (id,))
                if cursor.fetchone() is None:
                    return jsonify({"success": False, "message": "Proveedor no encontrado"}), 404
                # Esquema oficial: pedido_compra.id_proveedor; confirmar con DESCRIBE pedido_compra.
                cursor.execute("SELECT COUNT(*) AS total FROM pedido_compra WHERE id_proveedor = %s", (id,))
                if cursor.fetchone()["total"] > 0:
                    return jsonify({"success": False, "message": "No se puede eliminar el proveedor porque tiene compras registradas"}), 409
                cursor.execute("DELETE FROM proveedor WHERE id_proveedor = %s", (id,))
                conn.commit()
                return jsonify({"success": True, "message": "Proveedor eliminado correctamente"}), 200
    except Error as e:
        return error_bd(e)


@app.get("/api/health")
def health():
    try:
        with conexion() as conn:
            with conn.cursor() as cursor:
                cursor.execute("SELECT 1")
        return jsonify({"success": True, "service": "proveedores", "mysql": "connected"}), 200
    except Error as e:
        return error_bd(e)


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=int(os.getenv("PORT", "5000")), debug=False)
