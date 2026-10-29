# DeskBox: visibilidad, Hoy y captura rápida

Estado: diseño para revisión; ninguna mejora está implementada todavía.

## Objetivo y límites

Mejorar el trabajo diario en la copia personal jriveral1986-cpu/DeskBox. Conservar datos existentes, traducciones, arquitectura WinUI 3 y serialización compatible con Native AOT. Windows Smart App Control permanece activo. Compilación y pruebas se ejecutan en GitHub Actions. La base verificada es db3bddbb2e0148291e283a47a07a5bc3ad4d1a39: 5.887 pruebas aprobadas.

## Opciones consideradas

1. Integrar las tres funciones en los modelos, servicios y controles actuales. Recomendada: una sola fuente de datos y coherencia con los atajos y ajustes existentes.
2. Añadir una aplicación auxiliar de captura. Rechazada: introduce otro proceso, coordinación y almacenamiento sin necesidad.
3. Crear un panel nuevo que sustituya tareas y notas. Rechazada: duplica funciones y exige migrar datos.

Las tres mejoras se implementan en incrementos independientes, con integración y validación del conjunto al terminar.

## 1. Paneles siempre visibles

### Comportamiento

- Añadir una opción por panel: «Mantener visible al ocultar los demás». Desactivada por defecto para preservar el comportamiento anterior.
- La opción excluye ese panel de las acciones globales de ocultación del usuario: atajo, bandeja y doble clic en el escritorio.
- No significa «siempre encima» de otras aplicaciones; mantiene el modo de capas de Windows elegido por el usuario.
- Ocultar, cerrar o desactivar explícitamente ese panel sigue funcionando. Un panel ya oculto no reaparece por tener la opción marcada.
- Cerrar la aplicación, bloquear la sesión, desconectar una pantalla y terminar una sesión temporal Quick Reveal conservan sus reglas actuales. La excepción no mantiene ventanas temporales encima al perder foco ni fuerza a mostrar paneles en pantallas desconectadas.
- El siguiente comando global después de ocultar muestra otra vez los paneles normales. Los paneles excluidos no deben hacer que el conmutador quede atrapado en «ocultar».
- Si todos los paneles están excluidos, el comando global no cambia su visibilidad ni los desmarca.
- Los grupos se tratan como una sola superficie: la opción se aplica a la superficie del grupo; no se separan miembros ni se modifican sus carpetas. Al disolver un grupo, se conserva la preferencia de cada panel individual.

### Integración

Persistir una propiedad booleana con valor predeterminado false en WidgetConfig, y resolver explícitamente la preferencia de superficies agrupadas. Añadir la opción al menú contextual existente de paneles y grupos. Separar el conjunto participante del conmutador del conjunto de ventanas actualmente visibles. Conservar la cola serializada de WidgetManager.ToggleWidgetsFromTrayAsync y distinguir ocultación global del usuario de cierre, desactivación y limpieza del sistema.

### Pruebas

Combinaciones de paneles excluidos y normales; todos excluidos; panel excluido previamente oculto; mostrar después de ocultar; doble pulsación rápida; persistencia en JSON antiguo y nuevo; grupos y separación; limpieza de Quick Reveal; panel desactivado y pantalla desconectada. Verificar la lógica mediante políticas puras y pruebas de integración con el controlador actual.

## 2. Hoy mejorado

### Comportamiento

- Reutilizar la pestaña Hoy. Mostrar secciones «Principales», «Vencidas», «Hoy» y «Próximas»; ocultar encabezados sin elementos.
- Permitir elegir hasta tres tareas pendientes como principales para la fecha local actual, desde el menú de cada tarea. No cambiar su fecha de vencimiento ni su marca de importancia.
- Las principales aparecen una sola vez, en su sección. Completar o eliminar una libera el espacio; al comenzar otro día dejan de ser principales sin cambiar la tarea original.
- Vencidas contiene tareas pendientes con fecha anterior al día local actual, en orden de vencimiento ascendente.
- Hoy contiene tareas con vencimiento en la fecha local actual, ordenadas por hora. Próximas muestra los siguientes siete días locales, del día siguiente al séptimo inclusive, ordenados por vencimiento.
- Una tarea sin fecha aparece en Hoy solo si está elegida como principal. Las demás permanecen en las vistas existentes.
- Respetar el filtro de color. Las completadas se ocultan por defecto; si la preferencia existente permite mostrarlas, aparecerán después de las pendientes en su sección temporal y nunca ocuparán un espacio principal.
- Las vistas Todas, Activas, Importantes y Completadas conservan su orden manual. Reordenar por fecha en Hoy no reescribe SortOrder. El arrastre de orden manual no se ofrece en Hoy mientras el orden por fecha esté activo.
- Actualizar encabezados, contadores y selección al editar, completar, eliminar, restaurar y cambiar el día local; recalcular también al recuperar la aplicación después de suspensión.

### Integración

Una política de agenda recibe tareas y una fecha local explícita, sin leer el reloj estático durante sus pruebas. Persistir la fecha de selección principal en TodoItem y notificar cambios desde TodoItemViewModel. Reutilizar TodoWidgetViewModel, su colección y la lógica de guardado, sin una segunda base de tareas. La presentación agrupada debe adaptarse a las vistas de uno y dos paneles. Ajustar las pruebas antiguas que fijan la semántica anterior de Hoy explicando el cambio esperado.

### Pruebas

Vencidas, límites de siete días, fechas locales frente a UTC, cambio de mes/año, tareas sin fecha, tres principales y rechazo de una cuarta sin pérdida de datos, finalización, eliminación, recurrencia sin heredar selección principal, filtro de color, orden manual intacto, compatibilidad de datos antiguos y actualización al cambiar de día.

## 3. Captura rápida por teclado

### Comportamiento

- Añadir una opción en Ajustes > Atajos para activar y personalizar «Captura rápida». Proponer Ctrl+Alt+N como valor inicial; no registrar el atajo hasta que el usuario active la función.
- Al invocarlo, abrir una ventana pequeña con foco en el texto y selector «Nota / Tarea». Recordar la última elección guardada.
- Guardar notas en Quick Capture mediante su servicio existente. Para tareas, permitir elegir un panel de tareas existente y recordar ese destino por identificador estable.
- Si no hay panel de tareas disponible, explicar la ausencia y ofrecer crear uno explícitamente; no guardar en un destino arbitrario ni perder el borrador.
- Enter guarda una entrada simple; Shift+Enter inserta una línea; Ctrl+Enter también guarda; Escape cierra conservando el borrador de esta sesión. Mostrar estas instrucciones junto al editor.
- Tras guardar correctamente, cerrar la ventana y devolver el foco a la aplicación anterior cuando siga disponible. La nueva nota o tarea aparece inmediatamente en su panel.
- Invocaciones repetidas enfocan la misma ventana y conservan el borrador. No crean ventanas o registros duplicados. Bloquear el botón durante el guardado y mantener texto y destino si falla.
- Texto vacío o solo espacios no se guarda. No leer ni capturar el portapapeles automáticamente.
- Una colisión de atajos muestra el error y conserva el atajo anterior válido; nunca interceptar una combinación reservada mediante un nuevo hook.
- Liberar el registro y suscripciones al desactivar o cerrar DeskBox. Respetar la captura de teclas del editor de atajos existente.

### Integración

Separar registro del atajo, controlador de ventana y servicio de guardado. Reutilizar RegisterHotKey y la infraestructura de bandeja para combinaciones ordinarias, con identificador propio. Guardar notas con QuickCaptureService.AddDetailedItemWithResultAsync y tareas por el propietario existente de la colección para evitar que una escritura directa al archivo sea sobrescrita por la memoria. Usar un único guardado en curso; los fallos mantienen el borrador editable. No introducir servicios externos ni acceso a red.

### Pruebas

Valores predeterminados desactivados, persistencia de combinación y destino, colisión con búsqueda y visibilidad, destino eliminado o desactivado, texto vacío, guardado correcto de nota/tarea, error de disco, doble envío, reapertura con borrador, rechazo de atajo reservado, liberación al salir y notificación inmediata a la colección existente.

## Traducciones y accesibilidad

Añadir claves equivalentes para todos los idiomas actuales para mantener las comprobaciones de paridad. Español e inglés deben tener textos revisados; no dejar claves visibles en la interfaz. Los controles nuevos tendrán nombres accesibles, foco visible y navegación por teclado. Conservar contraste, escala de texto y estilos actuales.

## Verificación y entrega

1. Escribir pruebas de comportamiento primero y ejecutar la fase de fallo esperado en GitHub Actions antes del código que las satisface. No modificar Windows para correr Rust localmente.
2. Implementar cada incremento, ejecutar sus pruebas enfocadas y después toda la suite Windows x64.
3. Revisar configuración, migración de datos, suscripciones, errores de guardado, atajos, grupos y presentación.
4. Crear una rama de funciones y PR en la copia personal. No publicar cambios en el repositorio original.
5. Producir ZIP compilado, resultados TRX y enlace a la ejecución del commit final. Indicar el número exacto de pruebas y cualquier omisión.
6. La suite automatizada no sustituye una prueba visual interactiva. Si no hay un entorno gráfico autorizado para ejecutar la versión, entregar esa limitación explícitamente. No afirmar que la aplicación funciona visualmente en el equipo protegido.

## Fuera de alcance

Temporizador de concentración, organización virtual de archivos, sincronización remota, firma de código, instalador de distribución y cambios de políticas de Windows.
