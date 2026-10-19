# Autonomous Colonists 0.5 experimental: acciones y límites

Esta fase amplía v0.4 con un ejecutor real. La DLL llama a los generadores y al JobTracker de RimWorld 1.7; no contiene funciones vacías que cuenten propuestas como acciones. Compila contra las tres DLL proporcionadas (.NET 10), pero **aún no se ha ejecutado dentro del juego o en Android**. Las verificaciones automatizadas cubren el núcleo y su frontera, no sustituyen esa prueba.

## Funciones implementadas

| Sistema | Acción implementada | Condiciones y alcance |
| --- | --- | --- |
| Trabajo | Asignador v0.3 con habilidades/pasiones, especialistas, suplentes, carga y estabilidad | Conservado sin reescribir el algoritmo. Prioridad 0, bloqueos, incapacidades y exclusiones siguen protegidos. Advierte pacientes sin atención en curso, incendio importante sin extinción, falta de trabajador elegible o gestión pausada. No estima todas las facturas o necesidades económicas. |
| Comida | JobGiver_GetFood → Ingest automático | Filtra la política alimentaria respetada y el objeto/final ingestible, reservas, prohibiciones, zona, temperatura y ruta Danger.None. Admite inventario propio y dispensador válido. Rechaza caza, cosecha, rellenar tolvas y tomar comida de inventarios ajenos. |
| Descanso | JobGiver_GetRest → LayDown en cama | Comprueba GetPriority nativo para horario/posibilidad de dormir. Rechaza horas de Trabajo y dormir en suelo. No cambia horarios, camas asignadas ni políticas. |
| Medicina | WorkGiver_Tend de las definiciones cargadas → TendPatient | Prioriza urgencia médica y peor salud, busca colonos atendibles, respeta cuidados médicos, trabajo Doctor habilitado, capacidades y reservas. El juego elige la medicina; se valida su política y acceso. Puede atender sin medicina si el juego/política lo permiten. No opera ni interviene en animales/prisioneros. |
| Autocuidado | WorkGiver_TendSelf configurado → TendPatient propio | Exige selfTend ya autorizado y Doctor habilitado. No utiliza por sí solo JobGiver_SelfTend, cuyo cuerpo inspeccionado no comprueba el permiso. No cambia selfTend. |
| Recuperación médica | JobGiver_PatientGoToBed → LayDown | Usa necesidad médica real, BedRest habilitado, cama y horario permitido. No fuerza reposo indefinido ni cirugía. |
| Ropa | JobGiver_OptimizeApparel → Wear | El optimizador nativo considera deterioro, política, adecuación, piezas forzadas/bloqueadas y abrigo estacional. Se revalida política/objetivo y se respeta su temporizador. No ejecuta RemoveApparel, tintes, estilismo ni equipa armas. El calor extremo no tiene un optimizador propio. |
| Rescate | WorkGiver_RescueDowned configurado → Rescue | Solo colonos del jugador, no excluidos ni bajo orden manual, caídos fuera de cama. Requiere trabajo real habilitado en la definición, capacidad, reservas, cama y objetivos/rutas filtrados. El filtro nativo descarta enemigos cercanos; el director tampoco inicia rescates con señal global de peligro/incendio importante. |
| Seguridad | Flee y ExtinguishSelf | Usa FleeDanger/FleeFire para destino local, con validación de destino/zona/temperatura/ruta. Ante enemigos exige respuesta Huir del jugador. Fuego propio crea el job nativo sobre el fuego realmente adjunto al colono. No crea zonas ni recluta. |

El inventario de armas, defensa armada coordinada, ataques y órdenes militares automáticas **no están implementados**. Tampoco hay un planificador de refugios estratégicos/zonas de evacuación, tratamientos avanzados, cirugías, selección económica de medicamentos o evaluación completa del combate. No se expone una opción que pretenda habilitarlos.

## Arquitectura y autorización

1. AutonomousCoordinatorComponent observa colonos y señales de mapa en el hilo principal. Needs, Health, Apparel, Safety y el adaptador Work producen candidatos tipados.
2. AutonomousDecisionCoordinator clasifica, ordena, conserva incumbencia/histéresis y arbitra la actividad del colono. Equilibrado sigue valor predeterminado; Conservador/Avanzado cambian umbrales e intervalos.
3. Con ejecución autorizada, el resultado es **AwaitingValidation**, nunca una orden ejecutada. Con observación no se construye NativeActionRuntime ni se llaman buscadores nativos: algunos pueden modificar temporizadores.
4. ExecutionCoordinator aplica permisos y enfriamiento y solicita preparar solo unas pocas acciones. Si la principal no tiene alternativa utilizable, prueba candidatos inferiores del mismo colono. No convierte vetos manuales/estabilidad en autorización.
5. NativeActionRuntime consulta los generadores/WorkGiver originales. Una lista cerrada de JobDef impide ejecutar acciones laterales devueltas por ellos. Cada trabajo preparado vuelve a validar actor, objetivo y políticas; comprueba reservas antes de interrumpir nada y revalida después.
6. StartJob se llama con job automático, sin playerForced, ignoreForbidden ni ignoreDesignations. Nunca se utiliza TryTakeOrderedJob. Los buscadores temporales no se asignan a jobGiver: Job.ExposeData resolvería una clave de nodo a través de un ThinkTree guardado, al que estos objetos no pertenecen. workGiverDef sí conserva la definición original. El trabajo ordinario suspendible puede reanudarse mediante resumeCurJobAfterwards y la **cola nativa**; no se guardan referencias paralelas a jobs.
7. Solo un job aceptado como actual o en cola por el JobTracker incrementa Ejecutadas. El contador **no mide finalización**. Los trabajos rechazados liberan sus nuevas reservas y vuelven al pool. Tras transferirlo al juego, el mod no intenta liberarlo como si aún fuera suyo.

Se mantienen las restricciones en los tres perfiles:

- Director voluntario, observación y permiso de ejecución separados, todos por partida. Actualizaciones desde v0.3/v0.4 reciben acciones apagadas. Cada familia de acciones puede desactivarse; los candidatos desactivados no bloquean a otros módulos.
- Orden forzada actual, orden manual pendiente en cola, reclutamiento, exclusión, estado mental, incapacidad, carga transportada, cola no vacía, lord/duty y trabajo protegido impiden intervenir. También se protegen pacientes/objetivos excluidos o con orden manual.
- Normalmente solo interviene cuando está ocioso. Una urgencia crítica puede suspender trabajo ordinario interrumpible/suspendible; nunca trabajos de emergencia, combate, medicina, rescate, comida o sueño protegidos.
- Horarios y políticas permanecen en el juego. Rest no se fuerza durante Trabajo. Doctor/BedRest y el trabajo definido para rescatar deben tener prioridad positiva y capacidades válidas.
- Con señal global de peligro/incendio, las acciones nuevas de recursos y cuidados se suspenden; Safety gana prioridad en ejecución. Un destino local de huida puede ser rechazado si no cumple las restricciones. No existe garantía de seguridad total ante enemigos que se mueven después de la comprobación.
- Danger.None es el criterio de accesibilidad/región del juego, no un análisis completo de balística. Las áreas permitidas se respetan para destinos conforme al uso del juego; no se impone un corredor geométrico que nunca atraviese otra celda del mapa.

## Estabilidad, rendimiento y fallos

Se conservan los perfiles/histéresis de v0.4: cadencia 1250/750/250 ticks, margen de recuperación de necesidades de 5 puntos porcentuales e incumbencia ante mejoras marginales. La nueva ejecución admite respectivamente 1/2/3 acciones por evaluación, máximo cuatro preparaciones/búsquedas, rotación entre colonos y enfriamiento individual 2500/1250/750 ticks. Una búsqueda fallida espera 1000 ticks; ropa espera 5000, además del temporizador nativo. No se desactivan estos límites para necesidades críticas.

Listas, diccionarios y registros se reutilizan y se invalidan por salida, exclusión y mapa. Las búsquedas costosas solo ocurren para candidatos autorizados; los givers nativos pueden recorrer comida/ropa o calcular rutas, por lo que **cuatro búsquedas no garantizan un límite de milisegundos**. No hay tareas asíncronas, IA externa ni dependencias nuevas del mod. ILSpy se utilizó únicamente como herramienta privada de inspección; no se incorpora su código ni el del juego.

Una excepción en observación, preparación, reserva o inicio suspende el director. Mientras siga activo bloquea también escrituras del gestor; la recuperación requiere desactivar/reactivar después de revisar el registro. No se hace rollback artificial del mundo ni se cancela un job ya transferido al juego. Activar observación o desactivar ejecución impide nuevas acciones; las ya aceptadas siguen su ciclo nativo.

## Guardado y compatibilidad

AutonomousColonists.AutonomyDirector conserva exactamente su identidad y código histórico. El gestor y sus campos guardados se preservan. Los enums existentes no se reordenan: se añaden acciones al final.

El componente nuevo guarda configuración, memoria de clasificación/incumbencia y únicamente ID/mapa/último tick de ejecución por actor. No serializa jobs preparados, snapshots, actores temporales, reservas propias ni contadores/diarios derivados. La cola y los trabajos suspendidos los serializa el juego. Cambios de mapa invalidan también los reintentos fallidos, con regresión específica. Los valores faltantes conservan Equilibrado y acciones apagadas.

Las pruebas de representación y políticas pasan. Scribe real escribe XML con estos permisos y memoria; la lectura sigue bloqueada por dependencias originales ausentes. **Guardar/cargar partidas antiguas y reanudar trabajos en el juego siguen pendientes.** Ver PRUEBAS_0.5.md.
