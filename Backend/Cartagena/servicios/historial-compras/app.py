"""Historial independiente, seguro y alimentado por las ventas reales de MySQL."""
import os
from functools import wraps
import jwt
import mysql.connector
from mysql.connector import Error
from flask import Flask, jsonify, request, g
from flask_cors import CORS

app = Flask(__name__)
CORS(app, resources={r'/api/*': {'origins': os.getenv('CORS_ORIGIN','http://localhost:5173').split(',')}})
DB = {'host':os.getenv('DB_HOST','mysql'), 'port':int(os.getenv('DB_PORT','3306')),
      'user':os.getenv('DB_USER',''), 'password':os.getenv('DB_PASSWORD',''),
      'database':os.getenv('DB_NAME','tienda_online')}


class ApiError(Exception):
    def __init__(self, message, status):
        super().__init__(message)
        self.status = status


@app.errorhandler(ApiError)
def api_error(error):
    return jsonify(error=str(error)), error.status


@app.errorhandler(Error)
def sql_error(error):
    app.logger.exception('Error consultando ventas')
    return jsonify(error='No se pudo consultar MySQL'), 503


def auth(*roles):
    def decorate(func):
        @wraps(func)
        def wrapped(*args,**kwargs):
            header=request.headers.get('Authorization','')
            secret=os.getenv('JWT_SECRET','')
            if not header.startswith('Bearer ') or len(secret)<32:
                raise ApiError('Se requiere token',401)
            try:
                payload=jwt.decode(header[7:],secret,algorithms=['HS256'],
                  issuer=os.getenv('JWT_ISSUER','microservicio-login'),
                  options={'require':['sub','exp','iss']})
                g.uid=int(payload['sub'])
                g.role=str(payload.get('rol','')).upper()
            except (jwt.InvalidTokenError,ValueError,KeyError):
                raise ApiError('Token inválido',401)
            if roles and g.role not in roles:
                raise ApiError('Permisos insuficientes',403)
            return func(*args,**kwargs)
        return wrapped
    return decorate


def conexion():
    return mysql.connector.connect(**DB)


def ventas_usuario(uid):
    with conexion() as cn:
        with cn.cursor(dictionary=True) as c:
            c.execute('''SELECT v.id_venta,v.fecha,v.total,v.estado,v.estado_pago,
              e.estado AS estado_envio,e.numero_guia FROM venta v
              LEFT JOIN envio e ON e.id_venta=v.id_venta
              WHERE v.id_usuario=%s ORDER BY v.fecha DESC LIMIT 200''',(uid,))
            return c.fetchall()


@app.get('/health')
@app.get('/api/historial-compras/health')
def health():
    try:
        with conexion() as cn:
            with cn.cursor() as c:
                c.execute('SELECT 1')
                c.fetchone()
        return jsonify(ok=True,servicio='historial-compras')
    except Error:
        return jsonify(ok=False),503


@app.get('/api/historial-compras')
@auth()
def mis_compras():
    data=ventas_usuario(g.uid)
    return jsonify(usuario_id=g.uid,total_compras=len(data),compras=data)


@app.get('/api/historial-compras/usuario/<int:uid>')
@auth('ADMIN')
def compras_por_usuario(uid):
    data=ventas_usuario(uid)
    return jsonify(usuario_id=uid,total_compras=len(data),compras=data)


@app.get('/api/historial-compras/<int:oid>')
@auth()
def detalle(oid):
    with conexion() as cn:
        with cn.cursor(dictionary=True) as c:
            c.execute('SELECT * FROM venta WHERE id_venta=%s',(oid,))
            venta=c.fetchone()
            if not venta:
                raise ApiError('Compra no encontrada',404)
            if venta['id_usuario']!=g.uid and g.role!='ADMIN':
                raise ApiError('No puedes consultar compras ajenas',403)
            c.execute('''SELECT d.id_libro,l.titulo,l.autor,d.cantidad,d.precio_unitario
               FROM detalle_venta d JOIN libro l ON l.id_libro=d.id_libro
               WHERE d.id_venta=%s''',(oid,))
            items=c.fetchall()
    return jsonify(venta=venta,items=items)
