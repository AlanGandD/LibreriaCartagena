import { createInterface } from "node:readline/promises";
import { stdin, stdout } from "node:process";
import { variables } from "../src/config/entorno.js";
import { pool, cerrarPool } from "../src/config/baseDatos.js";
import { generarHash } from "../src/utils/passwords.js";
import {
  buscarUsuarioPorNombre,
  crearUsuario,
} from "../src/repositories/usuarios.repository.js";

const NOMBRE_USUARIO = "admin";
const CORREO = "admin@ventasonline.com";
const NOMBRE_COMPLETO = "Administrador General";
const ROL = "ADMIN";

async function solicitarContrasena() {
  const rl = createInterface({
    input: stdin,
    output: stdout,
    terminal: true,
  });

  try {
    const password = await rl.question(
      "Contraseña inicial del administrador: ",
    );

    if (password.length < 12) {
      throw new Error("La contraseña debe tener al menos 12 caracteres.");
    }

    return password;
  } finally {
    rl.close();
  }
}

async function ejecutar() {
  console.log("=== CREACIÓN DEL ADMINISTRADOR ===");
  console.log(`Base de datos: ${variables.DB_NAME}`);

  const existente = await buscarUsuarioPorNombre(NOMBRE_USUARIO);

  if (existente) {
    console.log(`El usuario "${NOMBRE_USUARIO}" ya existe. No se modificó.`);
    return;
  }

  const [roles] = await pool.execute(
    "SELECT id_rol FROM rol WHERE UPPER(nombre) = ? LIMIT 1",
    [ROL],
  );

  if (roles.length === 0) {
    throw new Error("No existe el rol ADMIN en MySQL.");
  }

  const password = await solicitarContrasena();
  const passwordHash = await generarHash(password);

  const idUsuario = await crearUsuario({
    nombreUsuario: NOMBRE_USUARIO,
    passwordHash,
    email: CORREO,
    nombreCompleto: NOMBRE_COMPLETO,
    telefono: null,
    rol: ROL,
  });

  console.log("\nAdministrador creado correctamente.");
  console.log(`ID: ${idUsuario}`);
  console.log(`Usuario: ${NOMBRE_USUARIO}`);
  console.log(`Rol: ${ROL}`);
  console.log("Contraseña: no se muestra por seguridad.");
}

ejecutar()
  .catch((error) => {
    console.error("Error al crear administrador:", error.message);
    process.exitCode = 1;
  })
  .finally(async () => {
    await cerrarPool().catch(() => {});
  });
