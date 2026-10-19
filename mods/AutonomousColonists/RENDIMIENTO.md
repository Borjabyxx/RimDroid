# Rendimiento v0.4: medición y límites

Entorno: Linux x64 del entorno de desarrollo, SDK .NET 10.0.402, Release, advertencias como errores. Son benchmarks sintéticos del núcleo puro; **no son mediciones del juego ni de Android**. No se ejecutan Verse, Scribe, UI, renderizado, rutas o jobs en el benchmark.

`Tests/Director/Program.cs` mide el director con 5, 20 y 50 colonos, dos necesidades importantes por colono y señal de amenaza en uno de cada cinco. Realiza 100 calentamientos y 1000 muestras; usa `Stopwatch.GetTimestamp` y `GC.GetAllocatedBytesForCurrentThread`. La muestra medida incluye clasificación, candidatos, arbitraje, memoria y diario, con ticks avanzados entre evaluaciones. Los snapshots ya están construidos.

Muestra durante el desarrollo del 19/10/2026:

| Colonos | Mediana ms | p95 ms | Bytes asignados/evaluación |
| --- | ---: | ---: | ---: |
| 5 | 0.0072 | 0.0081 | 4649 |
| 20 | 0.0439 | 0.0712 | 17089 |
| 50 | 0.1068 | 0.1696 | 41978 |

Son bytes **asignados**, no memoria retenida. Los resultados varían con JIT, CPU, carga y recolector; el JSON `director-tests.json` de cada build registra su muestra propia. No se fija un umbral de milisegundos frágil para hacer fallar CI según el runner.

El benchmark previo del asignador se conserva intacto: 1/5/20/50 colonos, 20 trabajos y 500 muestras; sus resultados están en `core-tests.json`. No sumar percentiles de ambos para atribuir un tiempo al juego.

## Coste y cachés

- En ticks no vencidos, el director solo consulta estado y contador: no recorre colonos y la prueba de 10 000 consultas registra cero bytes asignados en esa ruta.
- Intervalos por perfil 1250/750/250 ticks, configurables; no hay tareas asíncronas que accedan a Verse. Los botones son solicitudes explícitas del jugador.
- Una evaluación lee la lista de colonos desplegados y señales ya mantenidas por los watchers del juego; no recorre todos los objetos ni calcula rutas. No se instala un parche/evento de terceros para las necesidades: la API inspeccionada permite sondeo acotado sin nueva dependencia.
- Orden de colonos O(P log P). Tres módulos con un número pequeño de candidatos por actor; listas de observación/candidatos/resultados y conjunto de presencia reutilizados. Memoria O(P), diario de 64 entradas. Los motivos y snapshots de memoria generan asignaciones por evaluación, cuantificadas arriba; no por tick.
- Invalida memoria por exclusión/salida/mapa/perfil; no mantiene una caché de objetos del mapa que pueda quedar obsoleta. Las señales de emergencia son transitorias y no se restauran como si fueran una amenaza actual.
- El gestor v0.3 conserva su sondeo de prioridades cada 250 ticks y recálculo condicionado por intervalo/cambios. WorkAutonomy no obliga a recalcularlo con cada propuesta; solo comparte el permiso de escritura. Se conserva la caché de WorkTypeDef ordenados.

## Medición real pendiente

El panel muestra milisegundos y bytes asignados de **observación Verse + núcleo** de la última evaluación mediante los mismos contadores; solo al ejecutar el mod habrá valores representativos del juego. No se han medido aquí: no hay una sesión real ni dispositivo Android.

En RimDroid, usar copias de partidas con 5/20/50 colonos, registrar modelo/SoC/RAM, versión del juego/RimDroid, mapa, mods, perfil e intervalo, al menos 100 evaluaciones, TPS y pausas. Comparar director apagado, observación y gestor activo por separado. Verificar además temperatura/consumo y estabilidad tras varios días de juego. El contador de bytes no mide RSS ni memoria nativa; usar el perfilador del dispositivo si está disponible.
