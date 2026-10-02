# SIRASD 0.9 Escritorio

## Diseño actual

La aplicación funciona en una computadora Windows y almacena la información en una base SQLite local. La interfaz está desarrollada en C# con WPF y no contiene páginas HTML ni un navegador integrado.

Los registros usan identificadores globalmente únicos, fechas de creación y actualización, borrado lógico y bitácora. Esto permite agregar sincronización posteriormente sin reemplazar el modelo de información.

## Evolución a varias computadoras

Cuando el centro necesite trabajar desde varias PC, la aplicación conservará la misma interfaz y agregará:

1. Un servidor central protegido mediante HTTPS.
2. Una base de datos central PostgreSQL o SQL Server.
3. Sincronización de cambios mediante los identificadores y fechas existentes.
4. Control de conflictos y bitácora por usuario.
5. Copias de seguridad automáticas del servidor.

La base SQLite seguirá siendo útil como caché local y para continuidad temporal cuando no exista conexión.

## Protección de información

- Las contraseñas se derivan con PBKDF2-SHA256 y sal aleatoria.
- La base se guarda fuera de la carpeta del programa.
- Las consultas utilizan parámetros.
- Los respaldos deben conservarse en una memoria USB protegida.
- La etapa de varias computadoras requerirá cifrado en tránsito, permisos por función y políticas de sesión.
