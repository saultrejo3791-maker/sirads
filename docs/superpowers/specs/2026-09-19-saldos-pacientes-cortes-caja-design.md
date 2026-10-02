# Diseño: saldos de pacientes, cargos semanales y cortes de caja

Fecha: 19 de septiembre de 2026  
Estado: diseño conversacional aprobado; pendiente de revisión del documento por el usuario  
Producto: SIRASD Desktop 0.9.7, Windows, WPF y SQLite

## 1. Propósito

Extender SIRASD para que cada paciente tenga un saldo financiero visible y auditable, que pueda disminuir con los pagos registrados y, de forma opcional por paciente, aumentar automáticamente con la cuota semanal de cada lunes.

La misma ampliación permitirá consultar ingresos por folio y medio de pago, obtener totalizaciones diarias, semanales, mensuales o personalizadas, cerrar cortes definitivos y reimprimirlos en impresora térmica de 58/80 mm o en hoja normal/PDF.

La solución debe conservar el diseño actual de SIRASD y la información histórica. Los comprobantes y folios existentes no se reescriben.

## 2. Resultado esperado

El trabajo se considera correcto cuando:

- El saldo se ve desde la lista de pacientes, el expediente y el registro de pagos.
- El saldo puede ajustarse manualmente desde pacientes o pagos mediante el mismo flujo auditado.
- Cada paciente tiene una casilla individual para habilitar o deshabilitar cargos automáticos.
- La edición manual del saldo queda bloqueada mientras esa casilla está activa.
- La cuota se agrega una sola vez por lunes para pacientes activos con automatización habilitada.
- Las semanas pendientes se recuperan una sola vez después de un periodo con el programa cerrado.
- Los pagos pueden aplicarse o no al saldo, según una casilla marcada de forma predeterminada.
- Un pago mayor que la cuota reduce la deuda total; un pago mayor que toda la deuda crea saldo a favor.
- El saldo a favor cubre cargos semanales posteriores antes de crear una nueva deuda.
- Los pagos y cancelaciones conservan una historia inmutable y verificable.
- La vista de cortes desglosa todos los folios, medios de pago e importes y presenta totales correctos.
- Un corte cerrado conserva una copia definitiva, numerada y reimprimible.
- El corte completo puede imprimirse como ticket térmico, incluso cuando requiere varias páginas o tramos.
- Los tickets y cortes de 58 mm permanecen centrados dentro del ancho imprimible real y no cortan títulos ni importes.

## 3. Decisiones aprobadas

### 3.1 Saldo actual más historial de movimientos

SIRASD guardará un saldo actual por paciente para mostrarlo con rapidez y una bitácora financiera que explique cada cambio. No se dependerá solo de una cifra editable ni se recalculará todo el expediente para cada pantalla.

Convención interna:

- Un saldo positivo representa deuda pendiente.
- Cero representa una cuenta al corriente.
- Un saldo negativo representa saldo a favor.

La interfaz nunca mostrará un importe negativo sin explicación. Traducirá el valor a `Deuda`, `Sin saldo pendiente` o `Saldo a favor`.

### 3.2 Automatización individual

La automatización se controla por paciente. Al habilitarla, el primer cargo queda programado para el siguiente lunes posterior a la activación. Activarla no crea un cargo inmediato, incluso si se habilita durante un lunes.

La automatización solo opera mientras el paciente está en estado `Activo`. Marcarlo `Inactivo` detiene los cargos futuros y conserva el saldo existente. Al volver a `Activo`, el siguiente cargo se programa para el lunes posterior a la reactivación; las semanas inactivas no se cobran retroactivamente.

### 3.3 Aplicación de pagos

Cada pago incluye `Aplicar este pago al saldo`, activado de forma predeterminada.

- Marcado: el importe completo reduce el saldo del paciente.
- Desmarcado: el ingreso y su ticket se registran y aparecen en los cortes, pero el saldo no cambia.
- El importe no se limita a una cuota semanal.
- Si el pago rebasa toda la deuda, la diferencia queda como saldo a favor.

### 3.4 Cierre contable

La vista previa es una consulta dinámica. Un corte cerrado es una instantánea definitiva de movimientos de caja que no habían sido incluidos en otro cierre.

Los reportes semanales y mensuales pueden reunir varios cortes y movimientos todavía no cerrados. Esto no vuelve a asignar los folios a otro cierre.

### 3.5 Historial inmutable

Los pagos no se eliminan ni se sobrescriben. Una corrección de importe o medio de pago se resuelve así:

1. Cancelar el pago original con motivo obligatorio.
2. Crear una reversión vinculada al folio original.
3. Registrar un nuevo pago correcto con un nuevo folio, cuando corresponda.

Si el pago original ya pertenecía a un corte, ese corte no cambia. La reversión y el nuevo pago aparecen en el siguiente corte disponible.

## 4. Alcance de la interfaz

### 4.1 Continuidad visual

Las pantallas nuevas reutilizarán los recursos actuales:

- `Segoe UI`.
- Colores `Navy`, `Green`, `Surface`, `Ink`, `Muted`, `Line` y `Mint`.
- Tarjetas blancas redondeadas mediante el estilo `Card`.
- Botones normales y `PrimaryButton`.
- Tablas con filas alternadas, selección verde y encabezados existentes.
- Barra lateral y encabezado principal sin rediseño.

No se introduce una segunda identidad visual.

### 4.2 Lista de pacientes

La tabla `Directorio de pacientes` añadirá:

- `Saldo`: deuda, cuenta al corriente o saldo a favor, siempre en MXN.
- `Automático`: casilla interactiva por paciente.
- `Ajustar`: acción discreta por fila.

La tabla seguirá siendo de solo lectura para los demás campos. Cambiar la casilla mostrará una confirmación y guardará la decisión de inmediato. El botón `Ajustar` se deshabilita cuando la automatización está activa.

La acción general `Editar seleccionado` y el doble clic abrirán el formulario actual ampliado con:

- Cuota semanal.
- Estado de la automatización.
- Saldo actual.
- Próximo lunes programado o último lunes procesado.
- `Ver movimientos del saldo`.
- `Ajustar saldo`, habilitado solo en modo manual.

### 4.3 Ajuste de saldo

La lista de pacientes y la pantalla de pagos abrirán el mismo diálogo. El diálogo solicitará:

- Saldo actual, no editable.
- Nuevo saldo.
- Diferencia calculada.
- Motivo obligatorio.
- Confirmación final.

El usuario autenticado, fecha y hora se toman de la sesión. No se permitirá guardar un ajuste sin motivo ni mientras la automatización esté activa.

### 4.4 Pagos y tickets

Se conserva la composición actual: formulario en tarjeta izquierda e historial en tarjeta derecha.

Al seleccionar un paciente se mostrará un resumen con:

- Deuda o saldo a favor.
- Cuota semanal.
- Automatización activa o manual.
- Último cargo semanal.

Los controles actuales `Saldo anterior` y `Saldo restante` se convierten en valores calculados y protegidos. Se agrega `Aplicar este pago al saldo` y una proyección del resultado antes de guardar.

El botón `Ajustar saldo` abre el diálogo común y se bloquea en modo automático.

### 4.5 Cortes de caja

Dentro de `Pagos y tickets` se agrega una segunda vista con los mismos patrones visuales, sin crear una sección ajena en la barra lateral.

La vista tendrá dos modos:

- `Vista previa`.
- `Cortes cerrados`.

Filtros rápidos:

- Hoy.
- Esta semana, de lunes a domingo.
- Este mes.
- Periodo personalizado, inclusivo por fecha local.

Tarjetas de resumen:

- Efectivo.
- Transferencia.
- Depósito.
- Tarjeta.
- Otros.
- Cancelaciones de caja.
- Total general neto.
- Cantidad de folios.

La tabla detallada incluirá:

- Folio.
- Fecha y hora.
- Paciente.
- Persona registrada en el comprobante.
- Concepto.
- Periodo.
- Medio de pago.
- Importe.
- Aplicación al saldo.
- Saldo resultante.
- Estado.
- Corte asociado, cuando exista.

Los ajustes manuales del saldo que no representan entrada o salida de dinero solo aparecen en la bitácora financiera del paciente. No alteran los totales de caja. Las cancelaciones de pagos sí aparecen como movimientos negativos de caja.

## 5. Modelo de datos propuesto

Los nombres exactos pueden adaptarse a las convenciones del código durante la planificación, pero deben conservar estas responsabilidades.

### 5.1 Ampliación de `Patients`

- `BalanceCents INTEGER NOT NULL DEFAULT 0`.
- `AutoDebtEnabled INTEGER NOT NULL DEFAULT 0`.
- `NextAutoChargeDate TEXT NULL`.
- `LastAutoChargeDate TEXT NULL`.
- `FinancialUpdatedAt TEXT NULL`.

Todos los importes se guardan en centavos enteros para evitar errores de redondeo.

### 5.2 `PatientBalanceMovements`

Bitácora de cambios de saldo:

- Identificador único.
- Paciente.
- Tipo: saldo inicial, cuota semanal, pago aplicado, cancelación de pago, ajuste manual u otra reversión definida.
- Importe con signo: positivo aumenta deuda y negativo la disminuye.
- Saldo anterior y saldo resultante.
- Lunes de la semana, solo para cuota automática.
- Referencia al pago u operación origen.
- Motivo.
- Usuario.
- Fecha y hora de creación.

Una restricción única para paciente, tipo `Cuota semanal` y lunes impide duplicados aunque la revisión se ejecute varias veces.

### 5.3 Ampliación de `Payments`

- `AppliesToBalance INTEGER NOT NULL DEFAULT 1` para pagos nuevos.
- `Status TEXT NOT NULL DEFAULT 'Vigente'`.
- `CancelledAt TEXT NULL`.
- `CancelledBy TEXT NULL`.
- `CancellationReason TEXT NULL`.

Los campos históricos de saldo anterior y restante permanecen como instantánea del momento del pago.

### 5.4 `PaymentReversals`

Registra la salida de caja que anula un pago sin modificar el original:

- Identificador.
- Pago y folio originales.
- Importe negativo.
- Medio de pago original.
- Motivo.
- Usuario.
- Fecha y hora.
- Corte en el que fue incluida, si ya se cerró.

Solo se permite una cancelación completa por pago vigente. Una corrección posterior se hace con un nuevo pago.

### 5.5 `CashCuts`

- Identificador.
- Número único con formato `CORTE-AAAAMMDD-NNN`.
- Inicio y fin del periodo consultado.
- Fecha y hora del cierre.
- Usuario.
- Observaciones opcionales.
- Totales guardados por medio de pago.
- Total de cancelaciones.
- Total neto.
- Cantidad de entradas.

### 5.6 `CashCutLines`

Instantánea de cada entrada o reversión incluida:

- Corte.
- Tipo de origen: pago o cancelación.
- Identificador del origen.
- Folio mostrado.
- Fecha y hora.
- Paciente mostrado.
- Concepto y periodo.
- Medio de pago.
- Importe con signo.
- Indicación de si el movimiento afectó el saldo del paciente.
- Saldo resultante en centavos.
- Estado.

La combinación de tipo e identificador de origen es única, por lo que un movimiento de caja solo puede pertenecer a un cierre definitivo.

## 6. Servicios y responsabilidades

### 6.1 Servicio de saldos

Responsable de:

- Leer el estado financiero de un paciente.
- Previsualizar un pago.
- Aplicar un pago y crear su movimiento.
- Crear un ajuste manual.
- Revertir el efecto de un pago cancelado.
- Traducir el saldo interno a deuda, cero o saldo a favor.

No imprime ni controla la interfaz.

### 6.2 Servicio de cargos semanales

Responsable de:

- Calcular el siguiente lunes posterior a una activación o reactivación.
- Revisar cargos vencidos al iniciar sesión, al entrar en pacientes o pagos y mediante una revisión diaria mientras la aplicación permanezca abierta.
- Agregar cada lunes pendiente hasta la fecha local actual.
- Avanzar la próxima fecha siete días después de cada cargo.
- Ignorar pacientes inactivos o con automatización deshabilitada.

Antes de desactivar la automatización o cambiar un paciente a inactivo, SIRASD procesará los lunes ya vencidos hasta la fecha de la operación. Después detendrá la programación. Esto evita perder el cargo del lunes actual por un cambio efectuado días después.

### 6.3 Servicio de pagos

La operación de guardar pago se amplía para ejecutar en una sola transacción SQLite:

1. Validar paciente, importe, concepto, periodo, medio de pago y receptor.
2. Asignar el siguiente folio.
3. Insertar el pago.
4. Aplicar el saldo cuando la casilla esté marcada.
5. Insertar el movimiento financiero.
6. Guardar las instantáneas de saldo anterior y resultante.
7. Confirmar todo o revertir todo.

### 6.4 Servicio de cortes

Responsable de:

- Consultar pagos y cancelaciones sin el límite visual de 500 registros.
- Aplicar filtros por fecha local.
- Agrupar importes por medio de pago.
- Separar total bruto, cancelaciones y total neto.
- Identificar movimientos cerrados y pendientes.
- Validar nuevamente la elegibilidad dentro de una transacción antes de cerrar.
- Crear número, cabecera y líneas del corte.
- Recuperar la instantánea guardada para consulta o reimpresión.

### 6.5 Impresión

El servicio actual de comprobantes conservará su función para tickets individuales. Un componente específico compondrá cortes de caja, reutilizando logo, tipografía, alineación y medición térmica.

## 7. Flujos principales

### 7.1 Activar automatización

1. El usuario marca la casilla del paciente.
2. SIRASD explica que no habrá cargo inmediato.
3. Al confirmar, guarda `AutoDebtEnabled` y programa el lunes posterior a la fecha local actual.
4. El ajuste manual queda bloqueado.
5. La acción queda en la bitácora general.

### 7.2 Desactivar automatización

1. SIRASD procesa primero cualquier lunes vencido hasta la fecha local actual.
2. El usuario confirma la desactivación.
3. Se elimina la próxima fecha programada y se conserva el saldo.
4. El ajuste manual vuelve a habilitarse.

### 7.3 Cambio a inactivo

1. SIRASD procesa primero los lunes vencidos mientras todavía estaba activo.
2. Guarda el estado `Inactivo` y pausa la programación.
3. No borra deuda ni saldo a favor.

Al reactivarlo, programa el siguiente lunes posterior a la reactivación sin recuperar semanas inactivas.

### 7.4 Registrar pago aplicado

Ejemplo:

- Deuda anterior: $2,000 MXN.
- Pago: $700 MXN.
- Resultado: deuda de $1,300 MXN.

Si la deuda anterior es $500 MXN y el pago es $700 MXN, el resultado interno es -$200 y la interfaz muestra `Saldo a favor: $200 MXN`.

### 7.5 Cargo con saldo a favor

Ejemplo:

- Saldo a favor: $200 MXN.
- Cuota del lunes: $500 MXN.
- Resultado: deuda de $300 MXN.

Se guarda un solo movimiento de cuota por $500; el saldo resultante refleja el crédito ya existente.

### 7.6 Cancelar pago

1. El usuario abre el pago y solicita cancelación.
2. SIRASD muestra folio, importe, medio y efecto sobre saldo.
3. El usuario escribe un motivo y confirma.
4. En una transacción se marca el pago, se crea la reversión de caja y, si el pago se aplicó al saldo, se revierte el movimiento financiero.
5. El folio original permanece y se puede reimprimir marcado `CANCELADO`.

### 7.7 Cerrar corte

1. El usuario selecciona el periodo.
2. La vista previa incluye movimientos vigentes y cancelaciones, e identifica los ya cerrados.
3. SIRASD presenta periodo, cantidad, primer y último folio, totales y cancelaciones.
4. Al confirmar, vuelve a validar los movimientos pendientes y crea el corte en una transacción.
5. El corte se abre listo para vista previa o impresión.

## 8. Reglas de cálculo de cortes

- Los pagos son entradas positivas aunque `Aplicar al saldo` esté desmarcado.
- Las cancelaciones son entradas negativas asociadas al medio de pago original.
- Los ajustes manuales del saldo no son caja y quedan excluidos.
- `Total neto por medio = pagos vigentes + cancelaciones de ese medio`.
- `Total general neto = suma de todos los medios`.
- La cantidad de folios distingue pagos y cancelaciones; la tabla permite rastrear ambas entradas.
- Las fechas inicial y final incluyen el día completo según la fecha local del centro.
- Una consulta puede contener movimientos cerrados y pendientes.
- Un cierre solo captura movimientos pendientes.
- Los registros históricos anteriores a esta actualización permanecen disponibles en consultas y reportes, pero no aparecen automáticamente como pendientes de un nuevo cierre. Los cierres formales comienzan con los movimientos creados desde la activación de esta función.

## 9. Impresión de tickets individuales

El comprobante conserva el logo adaptado y añade cuando corresponde:

- `SALDO ANTERIOR`.
- `PAGO APLICADO`.
- `SALDO RESTANTE`.
- `SALDO A FAVOR`.
- `Pago aplicado al saldo` o `Este pago no modificó el saldo`.
- Número de corte una vez asignado.
- Marca `CANCELADO` cuando proceda.

Los encabezados `COMPROBANTE DE PAGO`, `TOTAL PAGADO` y la moneda `MXN` permanecen completos, centrados y destacados.

## 10. Impresión térmica del corte

Formatos disponibles:

- Térmico de 58 mm.
- Térmico de 80 mm.
- Hoja normal/PDF.

Para 58 mm se usará el ancho imprimible comprobado de aproximadamente 48 mm, con margen interno de 1 mm por lado. El contenido no se compondrá usando los 58 mm nominales del rollo.

Orden del ticket:

1. Logo monocromático centrado.
2. `CORTE DE CAJA` centrado y destacado.
3. Número, periodo, fecha/hora y usuario.
4. Cantidad de folios.
5. Desglose completo de cada pago o cancelación.
6. Totales por medio de pago.
7. Cancelaciones.
8. `TOTAL GENERAL MXN` con mayor énfasis.
9. Líneas para `Entregó`, `Recibió` y `Firma`.
10. Leyenda final de SIRASD.

Cada línea de folio contiene como mínimo folio, fecha/hora, paciente, concepto, medio e importe MXN. Los textos largos se envuelven; no se ocultan datos esenciales.

Cuando el controlador térmico no admita una página continua de toda la longitud, el corte se dividirá en páginas o tramos numerados. Cada continuación repetirá el número de corte y mostrará `Página X de N`; los subtotales intermedios no sustituirán al total general final. No se truncará silenciosamente ningún folio.

La reimpresión usa siempre las líneas y totales guardados en el corte, no una consulta recalculada.

## 11. Migración y compatibilidad

### 11.1 Respaldo previo

Antes de la primera actualización de esquema, SIRASD creará una copia fechada y verificable de la base de la cuenta mediante un mecanismo seguro para SQLite. No se copiará una base abierta ignorando sus archivos WAL.

La migración será transaccional. Si falla, se conserva la versión de esquema anterior y se muestra un mensaje sin entrar en las pantallas nuevas.

### 11.2 Saldo inicial

Para cada paciente:

- Si existen pagos anteriores, se toma `RemainingBalanceCents` del comprobante más reciente, ordenado por fecha y folio.
- Si no existen, el saldo inicial es cero.
- Se crea un movimiento `Saldo inicial migrado` con la referencia al folio utilizado.
- Si un pago no puede asociarse con seguridad al paciente, no se usa para inferir su saldo y el incidente se registra para revisión.

Todas las automatizaciones comienzan desactivadas después de migrar. El usuario puede revisar el saldo y la cuota antes de activar cada paciente.

### 11.3 Registros históricos de caja

Los pagos anteriores se mantienen consultables y totalizables. Se marcan como históricos previos al inicio de cortes para evitar que la primera vista de pendientes intente cerrar toda la vida del sistema.

## 12. Seguridad y auditoría

La versión actual conserva el esquema de cuentas existente. Toda cuenta activa y autenticada puede realizar estas operaciones; no se agrega una jerarquía nueva de permisos en este alcance.

Quedan auditados:

- Activación o desactivación automática.
- Cambio de estado que pause o reanude cargos.
- Ajuste manual.
- Registro y cancelación de pago.
- Cierre y reimpresión de corte.

Los registros incluyen usuario, fecha, hora, acción y referencia. Los motivos son obligatorios para ajustes y cancelaciones.

## 13. Manejo de errores

- Todas las operaciones con varios registros usan una transacción inmediata de SQLite.
- Un fallo revierte pago, folio, saldo, movimiento y corte relacionados.
- Las restricciones únicas detectan duplicación de cuota y doble inclusión en cortes.
- Un conflicto se transforma en un mensaje comprensible y una recarga de la vista.
- Las vistas previas no modifican datos.
- Los errores de impresión no cambian el estado del pago ni del corte; permiten reintentar.
- SIRASD distingue entre `guardado`, `incluido en corte`, `enviado al controlador de impresión` y `comprobado físicamente`.

Mensajes previstos:

- `La cuota de esta semana ya fue agregada.`
- `El saldo no puede editarse mientras la deuda automática esté activa.`
- `El pago se registró, pero no se aplicó al saldo.`
- `Este movimiento ya pertenece al corte ...`
- `No hay movimientos pendientes para cerrar.`
- `No fue posible completar la operación; no se guardó ningún cambio.`

## 14. Pruebas

### 14.1 Pruebas unitarias

- Cálculo del siguiente lunes.
- Detección de semanas vencidas.
- Ausencia de duplicados.
- Pausa y reanudación por estado.
- Deuda, cero y saldo a favor.
- Pago aplicado y no aplicado.
- Pago mayor que cuota y mayor que deuda.
- Reversión de saldo por cancelación.
- Totales por medio y total neto.
- Numeración de cortes.
- División de impresión térmica sin pérdida de líneas.

### 14.2 Pruebas de integración con base temporal

- Migración desde el esquema actual.
- Saldo inicial desde el último comprobante.
- Transacción completa de pago y saldo.
- Recuperación de varias semanas con índice único.
- Cambio a inactivo y reactivación.
- Ajuste manual bloqueado o permitido según modo.
- Cancelación antes y después de un corte.
- Doble cierre concurrente del mismo movimiento.
- Consulta semanal/mensual a través de varios cortes.
- Restauración del respaldo previo.

### 14.3 Verificación visual

- Lista de pacientes a 1000 x 680 y 1240 x 820.
- Formulario de pago sin controles cortados.
- Estados de botón habilitado y bloqueado.
- Tablas con nombres, conceptos y periodos largos.
- Vista de cortes vacía, con pocos registros y con muchos registros.
- Ticket de 58 mm con logo y márgenes simétricos.
- Ticket de 80 mm.
- Hoja/PDF.
- `COMPROBANTE DE PAGO` y `CORTE DE CAJA` completos.
- Importes alineados y `MXN` visible.
- Corte térmico multipágina con todos los folios.

### 14.4 Prueba física

La compilación, las pruebas automáticas y la vista previa no prueban la salida real. La aceptación final de impresión requiere:

1. Reiniciar SIRASD con la compilación nueva.
2. Conectar la impresora correcta.
3. Imprimir un ticket individual de prueba.
4. Imprimir un corte de varios folios.
5. Verificar centrado, título completo, logo, cantidades y avance/corte de papel.

## 15. Criterios de aceptación

La función puede entregarse cuando:

- La migración se probó sobre una copia, conservando conteos y folios.
- Todos los escenarios financieros definidos tienen pruebas aprobadas.
- El usuario puede completar los flujos desde pacientes y desde pagos.
- No existe edición manual superpuesta con automatización activa.
- Ningún cargo semanal ni movimiento de corte se duplica.
- Los reportes coinciden con la suma independiente de sus líneas.
- Los comprobantes históricos permanecen disponibles.
- Las vistas nuevas respetan los recursos visuales existentes.
- Las vistas previas de 58, 80 y hoja/PDF no presentan recortes.
- La impresión física se informa por separado y solo se da por comprobada después de la prueba con el equipo.

## 16. Fuera de alcance

- Sincronización con una aplicación Android o servicios en la nube.
- Contabilidad fiscal, facturación electrónica o timbrado.
- Nuevos roles y permisos granulares.
- Eliminación física de pagos, cortes o movimientos.
- Modificación retroactiva de cortes cerrados.
- Cobro automático bancario.
- Cambio de la identidad visual general de SIRASD.

## 17. Entrega por etapas

La implementación deberá planearse en bloques verificables:

1. Migración, respaldo y motor de saldos.
2. Integración con pacientes y cargos de los lunes.
3. Integración transaccional con pagos, tickets y cancelaciones.
4. Vista previa, cierre e historial de cortes.
5. Impresión térmica/hoja y verificación integral.

Cada etapa debe preservar una base utilizable y pasar sus pruebas antes de avanzar.
