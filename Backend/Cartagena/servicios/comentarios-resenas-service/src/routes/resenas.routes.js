const express = require('express');
const crypto = require('node:crypto');
const router = express.Router();
const pool = require('../database');

function auth(req, res, next) {
  const secret = process.env.JWT_SECRET || '';
  const header = req.headers.authorization || '';
  if (!header.startsWith('Bearer ') || secret.length < 32) return res.status(401).json({error:'Falta autenticación'});
  try {
    const token = header.slice(7);
    const parts = token.split('.');
    if (parts.length !== 3) throw Error();
    const h = JSON.parse(Buffer.from(parts[0],'base64url').toString());
    if (h.alg !== 'HS256') throw Error();
    const expected = crypto.createHmac('sha256',secret).update(`${parts[0]}.${parts[1]}`).digest();
    const actual = Buffer.from(parts[2],'base64url');
    if (actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) throw Error();
    const p = JSON.parse(Buffer.from(parts[1],'base64url').toString());
    const now = Math.floor(Date.now()/1000);
    if (!Number.isInteger(p.exp) || p.exp <= now || (p.nbf && p.nbf > now)
        || p.iss !== (process.env.JWT_ISSUER || 'microservicio-login')
        || !Number.isInteger(Number(p.sub)) || Number(p.sub)<=0) throw Error();
    req.user = {id:Number(p.sub),role:String(p.rol||'').toUpperCase()};
    next();
  } catch {return res.status(401).json({error:'Token inválido o vencido'});}
}

const listed = `SELECT id_comentario AS id, id_libro AS id_producto, id_usuario AS id_cliente,
  calificacion, texto AS comentario, fecha FROM comentario`;
router.get('/', async (req,res)=>{
  try {
    let sql=listed,par=[];
    if (req.query.id_libro) {sql+=' WHERE id_libro=?';par.push(req.query.id_libro)}
    const [rows]=await pool.query(sql+' ORDER BY fecha DESC LIMIT 200',par);
    res.json(rows);
  } catch(e){console.error(e);res.status(500).json({error:'Error al consultar reseñas'});}
});
router.get('/:id',async (req,res)=>{
  try {
    const [rows]=await pool.query(listed+' WHERE id_comentario=?',[req.params.id]);
    if(!rows.length)return res.status(404).json({error:'Reseña no encontrada'});
    return res.json(rows[0]);
  } catch(e){console.error(e);return res.status(500).json({error:'Error de consulta'});}
});
router.post('/',auth,async (req,res)=>{
  const {id_producto,id_libro,calificacion,comentario,texto}=req.body||{};
  const lid=id_libro||id_producto, message=texto||comentario;
  const grade=Number(calificacion);
  if (!lid || typeof message!=='string' || !message.trim() || !Number.isInteger(grade) || grade<1 || grade>5)
    return res.status(400).json({error:'Se requiere libro, comentario y calificación entera entre 1 y 5'});
  try {
    const [purchases]=await pool.query(`SELECT 1 FROM venta v
      JOIN detalle_venta d ON d.id_venta=v.id_venta WHERE v.id_usuario=? AND d.id_libro=?
      AND v.estado IN ('Confirmado','Entregado') LIMIT 1`,[req.user.id,lid]);
    if (!purchases.length) return res.status(403).json({error:'Solo puede comentar quien haya comprado este libro'});
    const [result]=await pool.query(`INSERT INTO comentario (id_libro,id_usuario,calificacion,texto)
      VALUES (?,?,?,?)`,[lid,req.user.id,grade,message.trim()]);
    return res.status(201).json({id:result.insertId,mensaje:'Reseña creada'});
  } catch(e){console.error(e);return res.status(500).json({error:'No se pudo registrar la reseña'});}
});
router.put('/:id',auth,async (req,res)=>{
  const grade=Number(req.body?.calificacion), message=req.body?.comentario||req.body?.texto;
  if (!Number.isInteger(grade)||grade<1||grade>5||typeof message!=='string'||!message.trim())
    return res.status(400).json({error:'Calificación o comentario inválido'});
  try {
    const [result]=await pool.query(`UPDATE comentario SET calificacion=?,texto=?
       WHERE id_comentario=? AND (id_usuario=? OR ?='ADMIN')`,
      [grade,message.trim(),req.params.id,req.user.id,req.user.role]);
    if(!result.affectedRows)return res.status(404).json({error:'Reseña no encontrada o sin permisos'});
    return res.json({mensaje:'Reseña actualizada'});
  } catch(e){console.error(e);return res.status(500).json({error:'No se pudo actualizar'});}
});
router.delete('/:id',auth,async (req,res)=>{
  try {
    const [result]=await pool.query(`DELETE FROM comentario WHERE id_comentario=? AND (id_usuario=? OR ?='ADMIN')`,
      [req.params.id,req.user.id,req.user.role]);
    if(!result.affectedRows)return res.status(404).json({error:'Reseña no encontrada o sin permisos'});
    return res.json({mensaje:'Reseña eliminada'});
  } catch(e){console.error(e);return res.status(500).json({error:'No se pudo eliminar'});}
});
module.exports = router;
