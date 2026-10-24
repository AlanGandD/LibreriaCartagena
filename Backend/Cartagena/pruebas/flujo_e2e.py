#!/usr/bin/env python3
"""Flujo real de integración. CREA datos de prueba; utilizar base de DESARROLLO.
Variables ADMIN_USER ADMIN_PASSWORD CLIENT_USER CLIENT_PASSWORD; no almacenar secretos.
"""
import json
import os
import sys
import uuid
from urllib.request import Request, urlopen
from urllib.error import HTTPError

BASE=os.getenv('BASE_URL','http://localhost:8081').rstrip('/')


def call(method,path,token=None,body=None,key=None,expected=200,raw=False):
    headers={}
    if token:headers['Authorization']='Bearer '+token
    if key:headers['Idempotency-Key']=key
    data=None
    if body is not None:
        headers['Content-Type']='application/json'
        data=json.dumps(body).encode()
    req=Request(BASE+path,data=data,headers=headers,method=method)
    try:
        with urlopen(req,timeout=20) as r:
            status=r.status
            content=r.read()
    except HTTPError as e:
        status=e.code
        content=e.read()
    if status!=expected:
        raise AssertionError(f'{method} {path}: HTTP {status} (esperado {expected}); {content[:500]}')
    if raw:return content
    try:result=json.loads(content)
    except ValueError:result=content.decode(errors='replace')
    print(f'✓ {method} {path} -> {status}')
    return result


def login(prefix):
    user=os.environ[f'{prefix}_USER']
    password=os.environ[f'{prefix}_PASSWORD']
    result=call('POST','/api/v1/auth/login',body={'nombreUsuario':user,'contrasena':password})
    return result['datos']['token']


def main():
    for e in ['ADMIN_USER','ADMIN_PASSWORD','CLIENT_USER','CLIENT_PASSWORD']:
        if not os.environ.get(e):
            sys.exit(f'Falta {e}. Crea usuarios de prueba por la API; promociona admin desde MySQL.')
    suffix=uuid.uuid4().hex[:8]
    admin=login('ADMIN'); client=login('CLIENT')
    book_id='DEMO-'+suffix
    call('POST','/api/libros',admin,{'id_libro':book_id,'titulo':'Prueba Cartagena '+suffix,
         'autor':'Demo de software','precio':'199.00','stock':8,'editorial':'Presentacion'},expected=201)
    address=call('POST','/api/domicilios',client,{'calle':'Prueba','numero':'100',
        'colonia':'Centro','ciudad':'Los Mochis','estado':'Sinaloa','codigo_postal':'81200',
        'telefono':'6680000000'},expected=201)
    call('PUT','/api/carrito/'+book_id,client,{'cantidad':2})
    idem='e2e-'+uuid.uuid4().hex
    checkout={'id_domicilio':address['id_domicilio'],'tipo_pago':'Tarjeta','sandbox_resultado':'aprobado'}
    sale=call('POST','/api/checkout',client,checkout,idem,expected=201)
    repeat=call('POST','/api/checkout',client,checkout,idem,expected=200)
    assert repeat['repetido'] is True and repeat['id_venta']==sale['id_venta'], 'Idempotencia rota'
    book=call('GET','/api/libros/'+book_id)
    assert int(book['data']['stock'])==6, 'Stock incorrecto tras checkout'
    sale_id=sale['id_venta']
    history=call('GET','/api/historial-compras',client)
    assert any(v['id_venta']==sale_id for v in history['compras']), 'Falta historial'
    orders=call('GET','/api/pedidos',client)
    assert any(v['id_pedido']==sale_id for v in orders['data']), 'Falta en pedidos'
    package=call('POST','/api/paquetes',admin,{'id_venta':sale_id,'peso_kg':1.1,
        'largo_cm':25,'ancho_cm':18,'alto_cm':12},expected=201)
    package_id=package['id_paquete']
    call('PATCH',f'/api/paquetes/{package_id}',admin,{'estado':'Listo'})
    call('PATCH',f'/api/paquetes/{package_id}',admin,
         {'estado':'EntregadoAPaqueteria','numero_guia':'DEMO'+suffix})
    shipments=call('GET','/api/envios',admin)
    shipment=next(v for v in shipments['data'] if v['id_venta']==sale_id)
    call('PATCH',f'/api/envios/{shipment["id_envio"]}',admin,{'estado':'Entregado'})
    warranty=call('POST','/api/garantias',client,
        {'id_venta':sale_id,'id_libro':book_id,'motivo':'Prueba de garantía después de entrega'},expected=201)
    ticket=call('GET',f'/api/ventas/{sale_id}/ticket.pdf',client,raw=True)
    assert ticket.startswith(b'%PDF'), 'Ticket no es PDF'
    print('\nFLUJO COMPLETO VALIDADO: login -> libro -> domicilio -> carrito -> compra -> idempotencia -> stock -> historial -> paquete -> envío -> garantía -> ticket')
    print(f'Registros creados para demostración: libro {book_id}, venta {sale_id}, paquete {package_id}, garantía {warranty["id_solicitud"]}.')


if __name__=='__main__':
    try:main()
    except (AssertionError,KeyError,ValueError) as exc:
        sys.exit(f'Falló la prueba E2E: {exc}')
