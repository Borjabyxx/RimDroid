# Validación v0.5.1 experimental — 19/10/2026

## Ejecutado

- 40/40 pruebas C# v0.3 originales, sin editar Tests/Program.cs.
- 50/50 pruebas del director v0.4 originales, sin editar Tests/Director/Program.cs. Conservan semántica de ejecución apagada y propuestas sin ejecutor autorizado.
- 50/50 pruebas de módulos/frontera de ejecución (45 iniciales y cinco de v0.5.1). Integran núcleo y dispatcher con un runtime de pruebas que registra llamadas; **no ejecutan givers ni jobs nativos**. Las cinco nuevas verifican urgencia global, fallback detrás de otras emergencias, prioridad manual de Doctor, Medicina entre empates y precedencia de seguridad.
- 10/10 Python originales sin cambios, validando entrada privada, lista cerrada, rechazo de referencias propietarias, traversal, duplicados y symlinks.
- 1000 escenarios nuevos reproducibles de permisos/observación/exclusiones/peligro y ejecución; se conservan 400 escenarios del director, 200 del asignador y pruebas de estabilidad.
- Build real Release .NET 10 contra Assembly-CSharp 1.7.9786.4087, UnityStubs y Waylith del usuario, con advertencias como errores: cero advertencias y errores.
- Auditoría PE/ILOnly/TFM/dependencias y presencia de referencias a StartJob, TryIssueJobPackage, TryMakePreToilReservations, ClearReservationsForJob, ReturnToPool, AllowsMedicine y GetCurrentRespectedRestriction. Rechaza referencia a TryTakeOrderedJob. Esto prueba la frontera compilada, no su ejecución correcta en el juego.
- Benchmarks 5/20/50 del núcleo/dispatcher; tiempos y bytes en JSON. No miden costes nativos, Verse o Android.
- ZIP por lista cerrada con DLL propia, About, licencia, instalación, tres documentos v0.5 y cinco informes automatizados. No copia ni publica DLL privadas. SHA-256 se entrega con el build final.

Los resultados de cada build se incluyen en TestResults del ZIP y como artefactos públicos. CI ejecuta las suites puras en workflows separados; no necesita ni publica referencias privadas. El build real se realiza en el entorno preparado; no se configura un runner privado.

## Casos nuevos comprobados en el núcleo

Alimentación, descanso, cuidados, autocuidado autorizado, reposo médico, ropa, rescate y seguridad pasan por candidatos y autorización. Se comprueban confirmación de ejecución, fallback cuando no hay alternativas, un objetivo por lote, una acción por actor, reserva/objetivo invalidado en la frontera, orden manual aparecida durante preparación, mapa cambiado, cola/orden/duty/carga/protecciones, horario, peligro, límites de perfiles, cuatro búsquedas, rotación justa de 50 colonos, backoff de ropa, cooldown guardado, cambio de mapa, retroceso de ticks, memoria excluida/eliminada, módulos apagados y excepciones propagadas para suspensión.

Las pruebas de hechos/reservas de la frontera usan dobles de prueba. **No se afirma haber probado la reserva Verse, el consumo de comida/medicina ni una acción en Android.** No hay stubs de Verse en el mod o los tests públicos.

## Scribe y bloqueos demostrados

tools/scribe_smoke.py compila y ejecuta un ensayo contra Scribe real con estado sintético, sin partida. Comprueba escritura XML de gestor, director, permiso de ejecución y memoria de cooldown. La lectura está bloqueada por:

- com.rlabrecque.steamworks.net.dll, identidad 0.0.0.0, al resolver tipos del juego.
- Brutal.Core.Logging.dll, identidad 2026.9.0.0, al registrar el fallo.

Son dependencias originales del runtime. No se proporcionaron en mods.zip, que contiene exactamente las tres referencias de compilación. No se sustituyen ni se publican. Podrían aparecer más dependencias al resolver estas. Para validar todos los jobs hace falta además una sesión real del juego con sus Defs/mapa/estado; compilar las DLL o escribir XML no la sustituye.

## Prueba real en tablet pendiente

Usar copia de partida, registrar modelo/SoC/RAM, versión exacta de RimWorld/RimDroid, mods y registro del juego. Empezar con Core + este mod y Equilibrado.

1. Cargar v0.3/v0.4: trabajos conservados, director con permisos guardados y **acciones nuevas apagadas**. Otra partida nueva debe empezar apagada y Equilibrado.
2. Observación con todo el módulo autorizado: verificar que jobs, prioridades, colas, ropa, horarios y políticas no cambien por el mod. Los contadores de ejecución no aumentan.
3. Activar acciones, habilitando primero una familia cada vez. Comida: alimento permitido/prohibido, política estricta, inventario, dispensador, ausencia de comida, rutas peligrosas/zona excluida. No caza/cosecha automática.
4. Descanso: cama propia/disponible/ocupada, horarios Trabajo/Sueño/Cualquier cosa, sin camas y tarea protegida. No duerme en suelo ni modifica horarios.
5. Medicina: médico con Doctor a 0/positivo, emergencia y paciente en cama, NoCare/NoMeds/medicina limitada o ausente, dos médicos, autocuidado autorizado/desautorizado, exclusiones y capacidades. No cirugía ni permiso selfTend cambiado.
6. Ropa: deteriorada, permitida/prohibida, forzada/bloqueada, estación fría, prendas sin acceso y sin mejora clara. No quitar ropa, tintes ni cambios continuos. Comprobar especialmente clima cálido: queda limitado al criterio nativo.
7. Seguridad: no combatiente y respuesta Huir/Ignorar/Atacar, fuego propio, incendio cercano, destino fuera de zona, paciente caído con cama/sin cama y enemigo cercano. No recluta, cambia armas ni ataca automáticamente.
8. Dar órdenes forzadas actuales y en cola; reclutar; cargar objetos; ritual/caravana/duty; tareas médicas, fuego, comida o sueño en curso. Ninguna se reemplaza. Probar interrupción de trabajo ordinario suspendible solo ante crítica, reanudación y persistencia de cola nativa.
9. Guardar/cargar con acciones autorizadas, cooldown, suspensión por error y trabajo reanudable; cambiar mapa, excluir/reincluir y desactivar/reactivar. Probar migración v0.2 con una copia de guardado auténtico.
10. Comparar 5/20/50 colonos con director apagado/observación/acciones, registrar tiempos/memoria del panel y TPS, varios días de juego. Desactivar, restaurar prioridades y retirar el mod sobre otra copia.

Estado final: código de acciones y compilación disponibles; verificación funcional en juego y compatibilidad Android pendientes. Defensa armada/refugios estratégicos/equipamiento militar no se anuncian implementados.
