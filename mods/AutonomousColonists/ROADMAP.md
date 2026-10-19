# Próximas fases

1. Validar carga, opciones, órdenes forzadas, guardar/cargar, migración y restauración en RimWorld 1.7 real, en PC y Android por separado.
2. Medir el adaptador completo con 1/5/20/50 colonos en el dispositivo Android; el benchmark actual solo mide la lógica pura en Linux x64.
3. Configurar un runner privado para Actions; obtener y revisar el primer artefacto remoto sin referencias del juego.
4. Afinar cobertura a partir de demanda real de encargos, estaciones y reservas, con cachés e intervalos medidos. Hoy la demanda es configurable por el jugador.
5. Explorar interfaz de bloqueos por celda e integración voluntaria con otros gestores; no interceptar la UI sin justificarlo.
6. Investigar alimentación, descanso, medicina y ropa en fases separadas, respetando políticas y solo tras validar estabilidad del gestor de trabajo. No se anuncian como prestaciones actuales.

Invariantes: no publicar archivos del juego; no habilitar prioridades cero; preservar cambios manuales observables; restaurar solo celdas aún controladas; no crear/sustituir trabajos forzados ni alterar RimDroid; no atribuir al Android mediciones hechas en otro sistema.
