-- Estructura del archivo original adaptada a tienda_online.
-- EJECUCIÓN CONSERVADORA: no elimina tablas ni datos existentes.
-- ADVERTENCIA: CREATE TABLE IF NOT EXISTS no modifica tablas existentes;
-- comprueba conflictos y respalda los datos antes de aplicar.
-- Los nombres de tablas se conservan exactamente como en el archivo original.

CREATE DATABASE IF NOT EXISTS tienda_online CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE tienda_online;

-- 1. Tabla de Usuarios y Roles
CREATE TABLE IF NOT EXISTS rol (
    id_rol INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(50) NOT NULL UNIQUE
);

INSERT IGNORE INTO rol (nombre) VALUES ('Cliente'), ('Admin'), ('Proveedor');

CREATE TABLE IF NOT EXISTS usuario (
    id_usuario INT AUTO_INCREMENT PRIMARY KEY,
    id_rol INT NOT NULL,
    nombre VARCHAR(100) NOT NULL,
    correo VARCHAR(150) NOT NULL UNIQUE,
    contrasena_hash VARCHAR(255) NOT NULL,
    fecha_registro TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (id_rol) REFERENCES rol(id_rol)
);

-- 2. Domicilios de Usuarios
CREATE TABLE IF NOT EXISTS domicilio (
    id_domicilio INT AUTO_INCREMENT PRIMARY KEY,
    id_usuario INT NOT NULL,
    direccion VARCHAR(255) NOT NULL,
    ciudad VARCHAR(100) NOT NULL,
    estado VARCHAR(100),
    codigo_postal VARCHAR(20),
    FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE
);

-- 3. Catálogo e Inventario de Libros
CREATE TABLE IF NOT EXISTS libro (
    id_libro VARCHAR(50) PRIMARY KEY,
    titulo VARCHAR(255) NOT NULL,
    autor VARCHAR(150) NOT NULL,
    precio DECIMAL(10, 2) NOT NULL,
    stock INT NOT NULL DEFAULT 0,
    stock_minimo INT DEFAULT 5,
    stock_maximo INT DEFAULT 100,
    descripcion TEXT
);

-- 4. Comentarios y Calificaciones
CREATE TABLE IF NOT EXISTS comentario (
    id_comentario INT AUTO_INCREMENT PRIMARY KEY,
    id_libro VARCHAR(50) NOT NULL,
    id_usuario INT NOT NULL,
    texto TEXT NOT NULL,
    calificacion INT CHECK (calificacion BETWEEN 1 AND 5),
    fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (id_libro) REFERENCES libro(id_libro) ON DELETE CASCADE,
    FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE
);

-- 5. Carrito de Compras
CREATE TABLE IF NOT EXISTS carrito (
    id_carrito INT AUTO_INCREMENT PRIMARY KEY,
    id_usuario INT NOT NULL UNIQUE,
    total DECIMAL(10, 2) DEFAULT 0.00,
    FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS item_carrito (
    id_item INT AUTO_INCREMENT PRIMARY KEY,
    id_carrito INT NOT NULL,
    id_libro VARCHAR(50) NOT NULL,
    cantidad INT NOT NULL DEFAULT 1,
    FOREIGN KEY (id_carrito) REFERENCES carrito(id_carrito) ON DELETE CASCADE,
    FOREIGN KEY (id_libro) REFERENCES libro(id_libro)
);

-- 6. Tipos de Pago y Ventas
CREATE TABLE IF NOT EXISTS tipo_pago (
    id_tipo_pago INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(50) NOT NULL UNIQUE
);

INSERT IGNORE INTO tipo_pago (nombre) VALUES ('Tarjeta'), ('Transferencia');

CREATE TABLE IF NOT EXISTS venta (
    id_venta INT AUTO_INCREMENT PRIMARY KEY,
    id_usuario INT NOT NULL,
    id_tipo_pago INT NOT NULL,
    fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    total DECIMAL(10, 2) NOT NULL,
    FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario),
    FOREIGN KEY (id_tipo_pago) REFERENCES tipo_pago(id_tipo_pago)
);

CREATE TABLE IF NOT EXISTS detalle_venta (
    id_detalle INT AUTO_INCREMENT PRIMARY KEY,
    id_venta INT NOT NULL,
    id_libro VARCHAR(50) NOT NULL,
    cantidad INT NOT NULL,
    precio_unitario DECIMAL(10, 2) NOT NULL,
    FOREIGN KEY (id_venta) REFERENCES venta(id_venta) ON DELETE CASCADE,
    FOREIGN KEY (id_libro) REFERENCES libro(id_libro)
);

-- 7. Logística y Envíos
CREATE TABLE IF NOT EXISTS envio (
    id_envio INT AUTO_INCREMENT PRIMARY KEY,
    id_venta INT NOT NULL UNIQUE,
    id_domicilio INT NOT NULL,
    estado VARCHAR(50) NOT NULL DEFAULT 'Pendiente',
    FOREIGN KEY (id_venta) REFERENCES venta(id_venta) ON DELETE CASCADE,
    FOREIGN KEY (id_domicilio) REFERENCES domicilio(id_domicilio)
);

-- 8. Proveedores y Pedidos de Compra
CREATE TABLE IF NOT EXISTS proveedor (
    id_proveedor INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(150) NOT NULL,
    contacto VARCHAR(150),
    telefono VARCHAR(50)
);

CREATE TABLE IF NOT EXISTS pedido_compra (
    id_pedido INT AUTO_INCREMENT PRIMARY KEY,
    id_proveedor INT NOT NULL,
    fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    costo_total DECIMAL(10, 2) NOT NULL,
    FOREIGN KEY (id_proveedor) REFERENCES proveedor(id_proveedor)
);

CREATE TABLE IF NOT EXISTS detalle_pedido_compra (
    id_detalle_compra INT AUTO_INCREMENT PRIMARY KEY,
    id_pedido INT NOT NULL,
    id_libro VARCHAR(50) NOT NULL,
    cantidad INT NOT NULL,
    costo_unitario DECIMAL(10, 2) NOT NULL,
    FOREIGN KEY (id_pedido) REFERENCES pedido_compra(id_pedido) ON DELETE CASCADE,
    FOREIGN KEY (id_libro) REFERENCES libro(id_libro)
);