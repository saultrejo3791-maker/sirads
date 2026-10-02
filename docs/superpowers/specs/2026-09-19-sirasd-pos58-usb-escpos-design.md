# Diseño: SIRASD 0.9.8 con impresión ESC/POS USB de 58 mm

Fecha: 19 de septiembre de 2026

Estado: diseño conversacional aprobado; pendiente de revisión del documento por el usuario

Producto: SIRASD Desktop para Windows, WPF, .NET 10 y SQLite

## 1. Propósito

Preparar SIRASD 0.9.8 para registrar pagos y enviar comprobantes directamente a una impresora térmica USB de 58 mm mediante ESC/POS, sin abrir el cuadro de impresión de Windows para cada ticket.

La versión debe conservar el folio definitivo asignado por SIRASD, permitir guardar sin imprimir, guardar e imprimir, y reimprimir el mismo comprobante sin crear otro pago. También debe conservar la vista previa y la salida a hoja normal o PDF ya existentes.

La entrega incluirá un instalador, un ejecutable portátil, documentación de configuración y verificación de integridad. La prueba térmica usará un comprobante marcado como prueba y no registrará un pago real.

## 2. Estado de partida confirmado

La solución activa se encuentra en `C:\Users\A SOLAS CON DIOS A.C\Downloads\SIRASD_0.9_Escritorio_Etapa1` y corresponde a SIRASD Desktop 0.9.7.

La versión existente ya incluye:

- Pagos por cuenta en SQLite.
- Folios consecutivos con formato `PAG-000001`.
- Instantáneas históricas del paciente, centro, importe y saldos.
- Guardado de pagos en transacciones.
- Auditoría de impresiones y reimpresiones aceptadas.
- Comprobantes gráficos de 58 mm, 80 mm y hoja normal/PDF.
- Logotipo institucional en `Assets/CentroLogoTicket.png`.
- Vista previa, historial y reimpresión.

Windows reconoce actualmente:

- Cola recomendada: `POS-58`.
- Controlador: `POS-58 11.3.0.1`.
- Puerto: `USB001`.
- Dispositivo presente: `YICHIP3121 POS-58 Printer`.
- Colas no recomendadas: `POS-58 (1)` y `POS-58 (2)` en `COM4`.

La presencia del dispositivo y de la cola no demuestra por sí sola una impresión física correcta. La verificación final debe distinguir entre trabajo generado, trabajo aceptado por Windows, cola terminada y ticket observado en papel.

## 3. Resultado esperado

El trabajo se considerará listo para entrega cuando:

- SIRASD muestre los conceptos `Cuota semanal`, `Pago de ingreso`, `Abono a adeudo` y `Otros conceptos`.
- `Otros conceptos` exija una descripción y la conserve en el historial y el ticket.
- Cada pago permita elegir si su comprobante muestra saldo/deuda.
- La elección de mostrar saldo quede guardada con el pago y se respete en toda reimpresión.
- `Guardar pago` registre el movimiento sin imprimir.
- `Guardar e imprimir` guarde primero y después envíe el comprobante a la cola térmica seleccionada.
- `Reimprimir comprobante` use el pago seleccionado y conserve su folio original.
- El ticket incluya folio, fecha y hora, usuario, concepto, importe, forma de pago, saldo cuando corresponda, receptor y datos del Centro.
- El ticket use el logotipo institucional cuando el recurso esté disponible.
- El folio aparezca en texto y como CODE128.
- La impresora avance cuatro líneas al finalizar.
- Los errores básicos de impresora no dupliquen ni deshagan pagos guardados.
- El ticket de prueba no abra ni modifique la base real de pacientes y pagos.
- La versión se compile, verifique y publique como SIRASD 0.9.8.
- La entrega incluya instalador, ejecutable portátil, guía, notas y manifiesto SHA-256.

## 4. Alcance y exclusiones

### 4.1 Incluido

- Impresión ESC/POS directa a través de una cola de Windows.
- Formato térmico específico de 58 mm y 384 puntos.
- Logotipo y texto rasterizados para conservar acentos y distribución.
- CODE128 nativo para el folio.
- Selección y persistencia local de la impresora.
- Comprobación previa de estados que el controlador de Windows exponga.
- Auditoría de envíos y fallos ligados a pagos reales.
- Prueba aislada sin registro contable.
- Conservación del flujo gráfico actual para vista previa, 80 mm y hoja/PDF.

### 4.2 No incluido

- Emisión de folios fuera de SIRASD.
- Edición o sustitución silenciosa de pagos guardados.
- Cancelaciones contables, cargos semanales automáticos o cortes de caja.
- Impresión directa desde Android o Bluetooth.
- Comunicación USB que omita el controlador y la cola de Windows.
- Cortador automático; la unidad se tratará como equipo de corte manual.
- Lectura bidireccional de `DLE EOT` a través del spooler de Windows.
- Un código QR adicional; CODE128 cumple el requisito de identificación escaneable sin agregar una dependencia externa.
- Instalación o sustitución automática del controlador POS-58.

## 5. Decisiones principales

### 5.1 Transporte mediante cola RAW de Windows

SIRASD enviará bytes ESC/POS con tipo de datos `RAW` a la cola seleccionada. La implementación usará las funciones de impresión de Windows para abrir la cola, iniciar el documento, escribir todos los bytes y finalizar o cancelar el trabajo de forma segura.

No se escribirá directamente al dispositivo USB ni a `COM4`. Esto conserva la compatibilidad con el controlador instalado y permite elegir la cola por nombre.

### 5.2 Texto y logotipo rasterizados

El cuerpo visual del ticket se renderizará como imagen monocromática de 384 puntos de ancho y se enviará con `GS v 0` en bandas de altura limitada. Este diseño evita depender de una tabla de caracteres no confirmada para `ñ`, vocales acentuadas y nombres propios.

El logotipo formará parte del contenido rasterizado. Si el recurso institucional no puede cargarse, el ticket continuará con el nombre del Centro y registrará la omisión en el resultado de composición.

### 5.3 CODE128 nativo

Después del cuerpo rasterizado, SIRASD imprimirá el folio como CODE128 mediante `GS k`, usando el subconjunto B para el valor alfanumérico `PAG-000000`. El texto legible del folio también aparecerá en el cuerpo del ticket, por lo que el comprobante seguirá siendo identificable aunque no se pueda escanear.

### 5.4 Guardar antes de imprimir

`Guardar e imprimir` ejecutará estas operaciones en orden:

1. Validar el formulario.
2. Solicitar la confirmación del registro.
3. Guardar el pago y asignar su folio dentro de una transacción SQLite.
4. Confirmar la transacción.
5. Componer el ticket con la instantánea guardada.
6. Revisar la cola configurada.
7. Enviar el trabajo ESC/POS.
8. Auditar el resultado.

Una falla en los pasos de impresión no revertirá la transacción ni creará otro pago. La recuperación será `Reimprimir comprobante` sobre el folio existente.

## 6. Componentes y responsabilidades

### 6.1 `EscPosReceiptComposer`

Responsable de:

- Recibir una instantánea `Payment`, opciones térmicas y el indicador de reimpresión.
- Componer el contenido de 384 puntos.
- Aplicar logotipo, jerarquía visual y ajuste de textos largos.
- Omitir o incluir los saldos según el valor guardado con el pago.
- Producir inicialización, bandas raster, CODE128 y avance final.
- Crear el ticket de prueba sin consultar la base de datos.

No enumera impresoras, no guarda pagos y no llama a la API de Windows.

### 6.2 `WindowsRawPrintTransport`

Responsable de:

- Abrir una cola por su nombre exacto.
- Enviar un documento `RAW` con un nombre descriptivo.
- Verificar escrituras parciales y exigir que se escriba todo el contenido.
- Cerrar identificadores aun cuando ocurra un error.
- Devolver el identificador del trabajo cuando Windows lo proporcione.
- Traducir los errores Win32 a excepciones con contexto operativo.

No compone tickets y no interpreta pagos.

### 6.3 `ThermalPrinterService`

Responsable de:

- Enumerar las colas disponibles y mostrar nombre, controlador y puerto.
- Resolver la cola guardada o seleccionar `POS-58` en `USB001` como recomendación inicial.
- Bloquear el envío cuando Windows informe un estado no imprimible.
- Coordinar compositor, transporte y resultado para la interfaz.
- Distinguir `aceptado por Windows` de `impresión física confirmada`.

### 6.4 `PrinterSettingsService`

Responsable de guardar, por usuario de Windows:

- Nombre exacto de la cola.
- Ancho de 384 puntos.
- Cuatro líneas de avance.
- CODE128 activado.

La configuración se almacenará como JSON en `%LOCALAPPDATA%\SIRASD\printer-settings.json`. Se escribirá primero en un archivo temporal y después se reemplazará el archivo anterior para reducir el riesgo de corrupción. No contendrá información de pacientes ni pagos.

### 6.5 `ReceiptPrintService`

El servicio gráfico existente conservará:

- Vista previa.
- Formato de 80 mm mediante controlador.
- Hoja normal y Microsoft Print to PDF.

La impresión directa de 58 mm dejará de usar `PrintDialog` y se dirigirá al servicio térmico.

## 7. Interfaz de pagos

El formulario mantendrá la composición actual de tarjeta izquierda e historial derecho.

### 7.1 Campos

Orden del formulario:

1. Paciente o usuario.
2. Concepto.
3. Descripción de `Otros conceptos`, visible y obligatoria solo para esa opción.
4. Periodo o semana.
5. Importe.
6. Forma de pago.
7. Saldo anterior.
8. Saldo restante.
9. `Mostrar deuda/saldo en este comprobante`, activado de forma predeterminada.
10. Nombre de quien recibe.
11. Formato e impresora.

Las opciones de concepto serán exactamente:

- `Cuota semanal`.
- `Pago de ingreso`.
- `Abono a adeudo`.
- `Otros conceptos`.

Para `Otros conceptos`, el valor histórico se guardará como `Otros conceptos: <descripción>` sin introducir una tabla nueva.

### 7.2 Acciones

- `Guardar pago`: guarda y selecciona el nuevo folio sin imprimir.
- `Guardar e imprimir`: guarda, selecciona el nuevo folio y solicita impresión directa.
- `Reimprimir comprobante`: requiere una fila seleccionada y usa la instantánea guardada.
- `Vista previa / PDF`: abre el flujo gráfico existente.
- `Imprimir ticket de prueba`: usa la impresora seleccionada y no accede a la base de pagos.
- `Actualizar impresoras`: vuelve a enumerar las colas sin reiniciar SIRASD.

El botón actual `Reimprimir último comprobante` se convertirá en `Reimprimir comprobante` para evitar reimprimir un folio distinto del que el usuario está revisando.

### 7.3 Mensajes

Después de un envío aceptado:

`Pago PAG-000123 guardado. Trabajo enviado a POS-58; revise el ticket impreso.`

Después de guardar con una falla de papel:

`Pago PAG-000123 guardado, pero no se pudo imprimir: la impresora informa falta de papel. Coloque el rollo y use Reimprimir comprobante.`

Los mensajes no afirmarán `impreso correctamente` basándose solo en que Windows aceptó el trabajo.

## 8. Modelo de datos y migración

### 8.1 `PaymentDraft` y `Payment`

Ambos modelos incorporarán:

`bool IncludeBalanceOnReceipt`

El valor predeterminado será `true`.

### 8.2 Tabla `Payments`

Se agregará de forma idempotente:

```sql
IncludeBalanceOnReceipt INTEGER NOT NULL DEFAULT 1
  CHECK(IncludeBalanceOnReceipt IN (0,1))
```

Antes de ejecutar `ALTER TABLE`, SIRASD consultará `PRAGMA table_info(Payments)`. Las bases nuevas incluirán la columna en la definición inicial y las existentes la recibirán una sola vez.

Los registros históricos usarán el valor `1`, que conserva su comportamiento actual. No se recalcularán saldos, no se renumerarán folios y no se reescribirán instantáneas.

### 8.3 Auditoría

Para pagos reales se registrarán:

- `Comprobante enviado`: folio, cola, puerto, formato y reimpresión o impresión inicial.
- `Error al imprimir comprobante`: folio, cola y resumen no sensible del error.

El ticket de prueba no creará pagos, folios ni entradas de auditoría contable. Su resultado quedará solamente en el reporte de prueba y en el mensaje de la interfaz.

## 9. Formato del ticket de 58 mm

### 9.1 Medidas

- Rollo nominal: 58 mm.
- Ancho imprimible: 384 puntos a 203 dpi, aproximadamente 48 mm.
- Ancho de composición: 384 puntos.
- Margen visual interno: 8 puntos por lado cuando el contenido lo permita.
- Color: un bit, negro sobre blanco.
- Corte: manual.
- Avance final: cuatro líneas mediante `ESC d 4`.

Las bandas raster se limitarán a una altura que evite saturar el búfer de una impresora económica. Cada banda conservará el ancho de 48 bytes.

### 9.2 Contenido

Orden:

1. Inicialización `ESC @`.
2. Logotipo institucional centrado, si puede cargarse.
3. `A SOLAS CON DIOS A.C.` y datos del Centro guardados con el pago.
4. `COMPROBANTE DE PAGO` o `PRUEBA DE IMPRESIÓN`.
5. Marca `REIMPRESIÓN` cuando corresponda.
6. Folio.
7. Fecha y hora.
8. Paciente o usuario.
9. Concepto y periodo.
10. `TOTAL PAGADO` e importe en MXN.
11. Forma de pago.
12. Saldo anterior y saldo restante, solo cuando `IncludeBalanceOnReceipt` sea verdadero.
13. Nombre de quien recibe.
14. Nombre de quien registra.
15. Leyenda institucional.
16. CODE128 del folio.
17. Texto legible del folio.
18. Avance de cuatro líneas.

El ticket de prueba sustituirá los datos reales por valores inequívocos y repetirá:

`PRUEBA - NO ES UN COMPROBANTE DE PAGO`

`NO REGISTRA NINGUN MOVIMIENTO`

El texto rasterizado usará tipografía legible, títulos en negrita y ajuste de línea. Los textos largos se envolverán; no se recortarán silenciosamente los campos obligatorios.

## 10. Selección de impresora

La resolución inicial seguirá este orden:

1. Usar la cola guardada si todavía existe.
2. Preferir el nombre exacto `POS-58` cuando su puerto empiece con `USB`.
3. Ofrecer otras colas cuyo nombre o controlador contenga `POS-58`.
4. Dejar la selección vacía y bloquear la impresión directa si no existe una coincidencia.

La interfaz mostrará nombre y puerto. Nunca seleccionará automáticamente `POS-58 (1)` o `POS-58 (2)` en `COM4` mientras `POS-58` en `USB001` esté disponible.

Cambiar la impresora no modificará pagos ni comprobantes guardados.

## 11. Estados y errores

Antes de enviar, SIRASD actualizará la cola y evaluará al menos:

- `Paused`.
- `Offline`.
- `PaperOut`.
- `PaperProblem`.
- `DoorOpen`.
- `Error`.
- `NotAvailable`.
- `UserIntervention`.

Cuando cualquiera de esos estados esté presente, el servicio no enviará bytes y devolverá una instrucción en español.

Si el controlador no expone el estado físico, SIRASD podrá enviar el trabajo y reportar solamente que Windows lo aceptó. No se usará `DLE EOT` a través de la cola RAW porque esa ruta no ofrece de forma fiable una respuesta bidireccional.

Errores previstos:

| Condición | Comportamiento |
|---|---|
| Cola inexistente | Bloquear el envío y pedir actualizar o seleccionar una impresora |
| Cola pausada | Pedir reanudarla en Windows |
| Sin papel o problema de papel | Conservar el pago y ofrecer reimpresión |
| Fuera de línea o desconectada | Conservar el pago y pedir revisar USB y encendido |
| Tapa abierta o intervención requerida | Conservar el pago y mostrar la condición |
| Error al abrir la cola | Informar nombre de cola y error de Windows |
| Escritura RAW parcial | Cancelar el trabajo y tratarlo como fallo |
| Falla al cerrar el trabajo | Informar resultado indeterminado; no crear otro pago |
| Configuración corrupta | Ignorar el archivo, conservarlo para diagnóstico y solicitar una nueva selección |
| Logotipo no disponible | Imprimir sin logo y conservar los datos textuales del Centro |

## 12. Ticket de prueba aislado

La misma función estará disponible desde la interfaz y mediante un argumento interno de verificación para la entrega.

La prueba:

- Construirá un objeto de comprobante en memoria.
- No inicializará `AccountService` ni `DatabaseService`.
- No leerá ni escribirá `%LOCALAPPDATA%\SIRASD\Accounts.db`.
- No creará un folio real.
- Usará `PRUEBA-POS58` como identificador visual y de CODE128.
- Emitirá un reporte con cola, puerto, fecha, bytes enviados, identificador de trabajo y resultado de Windows.

La comprobación automatizada podrá demostrar composición, envío y estado de la cola. La verificación de papel, contraste, alineación, logo y lectura del CODE128 requerirá observación del ticket físico.

## 13. Estrategia de pruebas

La implementación seguirá ciclos de prueba fallida, implementación mínima y prueba aprobada.

### 13.1 Compositor ESC/POS

Pruebas con valores esperados independientes para verificar:

- Inicio con `ESC @`.
- Ancho raster exacto de 48 bytes por fila.
- División en bandas sin pérdida de filas.
- Presencia de CODE128 con el folio correcto.
- Final con `ESC d 4`.
- Inclusión u omisión de saldos.
- Marca de reimpresión.
- Ticket de prueba sin datos reales.
- Textos largos y caracteres españoles rasterizados sin desbordar 384 puntos.

### 13.2 Configuración y selección

Pruebas para:

- Preferir la configuración guardada cuando existe.
- Preferir `POS-58` en USB sobre duplicados en COM4.
- No inventar una impresora cuando ninguna coincide.
- Recuperarse de un JSON inválido sin borrar datos de SIRASD.
- Escribir y volver a leer nombre, ancho, avance y CODE128.

### 13.3 Estados y transporte

El transporte se aislará detrás de una interfaz para comprobar:

- Bloqueo por pausa, desconexión, falta de papel, tapa abierta o error.
- Envío cuando la cola no informa una condición bloqueante.
- Detección de escritura parcial.
- Liberación de recursos en rutas de éxito y error.
- Mensajes que distinguen aceptación de Windows de impresión física.

Las pruebas automatizadas no enviarán bytes a la impresora real. La prueba física será un paso explícito y separado al final.

### 13.4 Datos y flujo de pagos

Con bases temporales se verificará:

- Migración idempotente de `IncludeBalanceOnReceipt`.
- Valor predeterminado verdadero para registros anteriores.
- Guardado y lectura del valor verdadero y falso.
- Los cuatro conceptos y la descripción obligatoria de `Otros conceptos`.
- Folio consecutivo sin duplicación.
- Reimpresión del mismo pago.
- Falla de impresión posterior al guardado sin segundo pago.
- Ticket de prueba sin cambios en las bases.

### 13.5 Verificación de aplicación y entrega

- Compilación Release completa.
- Ejecución de toda la suite de pruebas.
- Verificación aislada `--verify-installation` con base temporal.
- Generación y renderizado de una vista digital de 58 mm.
- Publicación autocontenida `win-x64` en un directorio limpio.
- Verificación del ejecutable portátil.
- Compilación del instalador.
- Cálculo y verificación de SHA-256.
- Envío de un ticket físico de prueba a `POS-58` en `USB001`.
- Consulta posterior de la cola para confirmar que no quedó un trabajo atascado.

## 14. Versión y paquete de entrega

La versión del proyecto, ensamblado e instalador pasará a `0.9.8` y `0.9.8.0` según corresponda.

La carpeta final será:

`SIRASD-0.9.8-POS58-USB`

Contenido:

- `SIRASD-0.9.8-Instalador-Windows-x64.exe`.
- `SIRASD-0.9.8-Portatil-Windows-x64.exe`.
- `LEEME-Instalacion-y-POS58.txt`.
- `NOTAS-DE-LA-VERSION-0.9.8.txt`.
- `AVISOS-TERCEROS.txt`.
- `SHA256.txt`.
- `Ticket-Prueba-POS58.png`.
- `REPORTE-VERIFICACION.txt`.

La guía explicará:

1. Encender la impresora y colocar el rollo de 58 mm.
2. Ejecutar la autoprueba del equipo con el botón `FEED` según su manual.
3. Verificar en Windows la cola `POS-58`, controlador `POS-58 11.3.0.1` y puerto `USB001`.
4. Evitar las colas duplicadas de `COM4` mientras la cola USB esté disponible.
5. Seleccionar o actualizar la impresora desde SIRASD.
6. Imprimir el ticket de prueba sin registrar un pago.
7. Registrar con `Guardar pago` o `Guardar e imprimir`.
8. Recuperar una falla mediante `Reimprimir comprobante`.
9. Usar `Vista previa / PDF` como respaldo.

## 15. Protección de información

- Las pruebas automáticas usarán datos ficticios y directorios temporales.
- El ticket físico de prueba no incluirá nombres, folios o importes reales.
- El paquete no incluirá ninguna base `SIRASD.db`, `Accounts.db`, respaldo o archivo de pacientes.
- Los mensajes de error y reportes no incluirán datos clínicos ni personales.
- La configuración de impresora no almacenará credenciales ni datos institucionales sensibles.
- El código conservará los archivos y funciones actuales fuera del alcance de pagos e impresión.

## 16. Criterios de aceptación

1. La suite completa termina sin fallos.
2. La compilación Release termina sin errores.
3. La verificación aislada termina con código cero y confirma pagos, folios, migración y composición ESC/POS.
4. La vista digital de 58 mm conserva logo, encabezado, campos, total y CODE128 dentro de 384 puntos.
5. El ticket de prueba no cambia el conteo de pagos ni el último folio de ninguna base real o temporal de control.
6. `Guardar pago` no imprime.
7. `Guardar e imprimir` crea un solo pago y después solicita un solo trabajo de impresión.
8. Una falla simulada de impresora conserva el pago y su folio.
9. `Reimprimir comprobante` no crea pagos ni folios y respeta la selección histórica de saldo.
10. `POS-58` en `USB001` se recomienda por encima de los duplicados en `COM4`.
11. El trabajo de prueba se envía a la cola elegida y la cola no queda atascada.
12. La observación física del papel se informa por separado; no se infiere de la cola.
13. El ejecutable portátil y el instalador existen, tienen versión 0.9.8 y coinciden con `SHA256.txt`.
14. La carpeta final contiene todos los archivos enumerados y ninguna base de datos real.
