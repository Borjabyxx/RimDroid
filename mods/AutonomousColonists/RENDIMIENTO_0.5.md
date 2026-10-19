# Rendimiento v0.5.1 y límites

Entorno: Linux x64, .NET SDK 10.0.402, Release, advertencias como errores. Se conserva el benchmark del asignador y del director; se añade núcleo con todos los módulos y dispatcher. **Ninguno mide los buscadores, reservas, drivers, rutas, UI o Android.** Los tests de ejecución usan un runtime que registra llamadas o devuelve ausencia de trabajo.

Muestra de desarrollo, 100 calentamientos y 500 evaluaciones por tamaño, snapshots de necesidades/salud/ropa previamente construidos:

| Colonos | Mediana ms | p95 ms | Bytes asignados/evaluación |
| --- | ---: | ---: | ---: |
| 5 | 0.0118 | 0.0152 | 4800 |
| 20 | 0.0414 | 0.0595 | 17672 |
| 50 | 0.1028 | 0.1783 | 43427 |

Cada build vuelve a medir: action-tests.json, director-tests.json y core-tests.json contienen su muestra propia y alcance. Los bytes son asignaciones de hilo administradas, no RSS/memoria nativa. No se fija un umbral de tiempo dependiente del runner para aparentar rendimiento uniforme.

## Coste real diseñado

- En ticks no vencidos: solo contador/estado, sin recorrer colonos. Cadencia 1250/750/250 ticks por perfil o intervalo del jugador.
- Observación: recorrido propio lineal de colonos, con consultas internas de salud/hediffs del juego; las consultas médicas pueden buscar médicos en casos de cirugía, así que no se atribuye un coste constante a todas las APIs nativas. Señales globales una vez por mapa. No recorre continuamente todos los objetos. El gestor conserva su intervalo/caché de trabajos; su aviso de pacientes sin médico actuando compara colonos (hasta O(P²)) solo durante el recálculo, no cada tick.
- Núcleo determinista O(P log P) con pocos candidatos por actor, listas reutilizadas, diario de 64 entradas y memoria por ID/mapa. Reintentos y estado se invalidan por salida, mapa y exclusión.
- Dispatcher: cola de prioridad global O(C log C) para C candidatos, con capacidad reutilizada; máximo cuatro preparaciones y 1/2/3 acciones aceptadas por evaluación. Enfriamiento individual 2500/1250/750 ticks. Ropa se limita además a 5000 ticks por reintento y respeta el temporizador nativo.
- Los generadores nativos sí pueden recorrer comida/ropa, consultar pacientes o calcular rutas cuando se autorizan. **Una sola búsqueda puede tardar más que el presupuesto deseado**; el límite de cuatro es de cantidad, no una garantía temporal. No se usan tareas asíncronas para intentar cortar Verse.
- El panel mide con Stopwatch y GC.GetAllocatedBytesForCurrentThread toda la observación + núcleo + búsquedas/ejecución solicitadas. Solo en una sesión real estos datos medirán el coste completo. Se muestran también búsquedas realizadas y acciones aceptadas.

En Android siguen pendientes mediana/p95 del adaptador completo, TPS, RSS, pausas GC, temperatura y consumo con 5/20/50 colonos y carga de objetos real. Comparar apagado/observación/acciones por familia. Los datos anteriores no garantizan rendimiento en la tablet.
